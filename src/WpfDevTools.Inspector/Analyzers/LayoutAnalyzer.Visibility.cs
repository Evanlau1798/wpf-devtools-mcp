using System.Windows;
using System.Windows.Media;
using WpfDevTools.Shared.ErrorHandling;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class LayoutAnalyzer
{
    /// <summary>
    /// Diagnose why a runtime element is or is not user-visible by checking self/ancestor visibility, opacity, clipping, layout size, and off-screen RenderTransform displacement.
    /// </summary>
    public object DiagnoseVisibility(string? elementId)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement frameworkElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target before diagnosing visibility.");
            }

            var checks = new List<object>();
            var selfVisibility = frameworkElement.Visibility.ToString();
            var selfOpacity = NormalizeDouble(frameworkElement.Opacity)?.ToString();
            checks.Add(CreateVisibilityCheck("selfVisibility", selfVisibility, frameworkElement.Visibility == Visibility.Visible, "Element Visibility"));
            checks.Add(CreateVisibilityCheck("selfOpacity", selfOpacity, frameworkElement.Opacity > 0, "Element Opacity"));

            var hasSize = frameworkElement.ActualWidth > 0 && frameworkElement.ActualHeight > 0;
            checks.Add(CreateVisibilityCheck("layoutSize", hasSize ? "positive" : "zero", hasSize, "ActualWidth/ActualHeight"));

            var isRenderTransformOffscreen = IsRenderTransformOffscreen(frameworkElement);
            checks.Add(CreateVisibilityCheck(
                "renderTransformViewport",
                isRenderTransformOffscreen ? "outside-viewport" : "inside-viewport",
                !isRenderTransformOffscreen,
                "RenderTransform-adjusted viewport bounds"));

            var clipping = AnalyzeClipping(frameworkElement);
            checks.Add(CreateVisibilityCheck("clipping", clipping.Severity, !clipping.IsFullyClipped, "Clipping"));

            var ancestors = EnumerateAncestorStates(frameworkElement);
            checks.AddRange(ancestors.Select(static ancestor => CreateVisibilityCheck(
                $"ancestor:{ancestor.elementId}",
                $"{ancestor.visibility} / {ancestor.opacity}",
                ancestor.isVisible,
                $"Ancestor {ancestor.displayName}")));

            var diagnosis = DetermineVisibilityDiagnosis(
                elementId ?? _elementFinder.GenerateElementId(frameworkElement),
                selfVisibility,
                frameworkElement.Opacity,
                hasSize,
                isRenderTransformOffscreen,
                clipping,
                ancestors);

            return new
            {
                success = true,
                elementId = elementId ?? _elementFinder.GenerateElementId(frameworkElement),
                isUserVisible = diagnosis.isUserVisible,
                checks,
                clipping = new
                {
                    severity = clipping.Severity,
                    isClipped = clipping.IsClipped,
                    isFullyClipped = clipping.IsFullyClipped,
                    visibleRatio = NormalizeDouble(clipping.VisibleRatio)
                },
                rootCause = diagnosis.rootCause,
                suggestedFix = diagnosis.suggestedFix
            };
        });
    }

    private static (bool isUserVisible, string? rootCause, string? suggestedFix) DetermineVisibilityDiagnosis(
        string elementId,
        string selfVisibility,
        double selfOpacity,
        bool hasSize,
        bool isRenderTransformOffscreen,
        ClippingDiagnosis clipping,
        IReadOnlyList<AncestorVisibilityState> ancestors)
    {
        if (!string.Equals(selfVisibility, Visibility.Visible.ToString(), StringComparison.Ordinal))
        {
            return (false, $"Element {elementId} has Visibility={selfVisibility}.", $"Set {elementId} Visibility to Visible.");
        }

        if (selfOpacity <= 0)
        {
            return (false, $"Element {elementId} has Opacity={selfOpacity}.", $"Increase {elementId} Opacity above 0.");
        }

        var ancestorVisibilityBlocker = ancestors.FirstOrDefault(static state => state.visibility != Visibility.Visible.ToString());
        if (ancestorVisibilityBlocker != null)
        {
            return (false,
                $"Ancestor {ancestorVisibilityBlocker.displayName} has Visibility={ancestorVisibilityBlocker.visibility}.",
                $"Set {ancestorVisibilityBlocker.displayName} Visibility to Visible.");
        }

        var ancestorOpacityBlocker = ancestors.FirstOrDefault(static state => state.opacity <= 0);
        if (ancestorOpacityBlocker != null)
        {
            return (false,
                $"Ancestor {ancestorOpacityBlocker.displayName} has Opacity={ancestorOpacityBlocker.opacity}.",
                $"Increase {ancestorOpacityBlocker.displayName} Opacity above 0.");
        }

        if (isRenderTransformOffscreen)
        {
            return (false,
                $"Element {elementId} is outside the visible viewport after applying its RenderTransform.",
                $"Review the RenderTransform offsets for {elementId} and move it back inside the visible viewport.");
        }

        if (clipping.IsFullyClipped)
        {
            return (false,
                $"Element {elementId} is fully clipped by its own or an ancestor clipping region.",
                $"Inspect clipping ancestors for {elementId} and relax ClipToBounds or sizing constraints.");
        }

        if (!hasSize)
        {
            return (false,
                $"Element {elementId} has zero layout size.",
                $"Review the layout constraints for {elementId} and ensure ActualWidth/ActualHeight are greater than 0.");
        }

        return (true, null, null);
    }

    private static ClippingDiagnosis AnalyzeClipping(FrameworkElement element)
    {
        var contentBounds = GetContentBounds(element);
        if (contentBounds.IsEmpty || contentBounds.Width <= 0 || contentBounds.Height <= 0)
        {
            return ClippingDiagnosis.None;
        }

        var diagnosis = ClippingDiagnosis.None;
        if (GetEffectiveClippingGeometry(element) is Geometry effectiveClip)
        {
            diagnosis = diagnosis.Merge(AnalyzeClipBoundary(contentBounds, effectiveClip.Bounds));
        }

        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current is Visual ancestorVisual)
        {
            if (current is UIElement ancestorElement &&
                GetEffectiveClippingGeometry(ancestorElement) is Geometry ancestorClip)
            {
                try
                {
                    var transformedBounds = element.TransformToAncestor(ancestorVisual).TransformBounds(contentBounds);
                    diagnosis = diagnosis.Merge(AnalyzeClipBoundary(transformedBounds, ancestorClip.Bounds));
                }
                catch (InvalidOperationException)
                {
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        if (TryGetWindowViewportBoundary(
                element,
                out _,
                out var transformedContentBounds,
                out var viewportBounds))
        {
            diagnosis = diagnosis.Merge(AnalyzeClipBoundary(transformedContentBounds, viewportBounds));
        }

        return diagnosis;
    }

    private static ClippingDiagnosis AnalyzeClipBoundary(Rect elementBounds, Rect clippingBounds)
    {
        if (clippingBounds.IsEmpty || clippingBounds.Width <= 0 || clippingBounds.Height <= 0)
        {
            return ClippingDiagnosis.Full;
        }

        var elementArea = elementBounds.Width * elementBounds.Height;
        if (elementArea <= 0)
        {
            return ClippingDiagnosis.None;
        }

        if (!elementBounds.IntersectsWith(clippingBounds))
        {
            return ClippingDiagnosis.Full;
        }

        var visibleBounds = Rect.Intersect(elementBounds, clippingBounds);
        if (visibleBounds.IsEmpty || visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
        {
            return ClippingDiagnosis.Full;
        }

        var visibleRatio = ClampRatio(visibleBounds.Width * visibleBounds.Height / elementArea);
        return visibleRatio >= 0.999
            ? ClippingDiagnosis.None
            : ClippingDiagnosis.Partial(visibleRatio);
    }

    private static double ClampRatio(double value)
    {
        if (value < 0d)
        {
            return 0d;
        }

        return value > 1d ? 1d : value;
    }

    private IReadOnlyList<AncestorVisibilityState> EnumerateAncestorStates(FrameworkElement element)
    {
        var ancestors = new List<AncestorVisibilityState>();
        DependencyObject? current = element;
        while (TryGetParent(current) is FrameworkElement parent)
        {
            ancestors.Add(new AncestorVisibilityState(
                _elementFinder.GenerateElementId(parent),
                parent.GetType().Name,
                string.IsNullOrWhiteSpace(parent.Name) ? null : parent.Name,
                parent.Visibility.ToString(),
                parent.Opacity));
            current = parent;
        }

        return ancestors;
    }

    private static DependencyObject? TryGetParent(DependencyObject current)
    {
        return LogicalTreeHelper.GetParent(current) ??
               (current is Visual visual ? VisualTreeHelper.GetParent(visual) : null);
    }

    private static object CreateVisibilityCheck(string key, string? value, bool passed, string description) => new
    {
        key,
        value,
        passed,
        description
    };

    private sealed record AncestorVisibilityState(
        string elementId,
        string elementType,
        string? elementName,
        string visibility,
        double opacity)
    {
        public bool isVisible => visibility == Visibility.Visible.ToString() && opacity > 0;

        public string displayName => !string.IsNullOrWhiteSpace(elementName)
            ? elementName!
            : $"{elementType} ({elementId})";
    }

    private sealed record ClippingDiagnosis(string Severity, double VisibleRatio)
    {
        public static ClippingDiagnosis None { get; } = new("none", 1d);

        public static ClippingDiagnosis Full { get; } = new("full", 0d);

        public bool IsClipped => Severity != "none";

        public bool IsFullyClipped => Severity == "full";

        public static ClippingDiagnosis Partial(double visibleRatio) =>
            new("partial", visibleRatio);

        public ClippingDiagnosis Merge(ClippingDiagnosis other)
        {
            if (IsFullyClipped || other.Severity == "none")
            {
                return this;
            }

            if (other.IsFullyClipped)
            {
                return other;
            }

            if (Severity == "none")
            {
                return other;
            }

            return Partial(Math.Min(VisibleRatio, other.VisibleRatio));
        }
    }
}
