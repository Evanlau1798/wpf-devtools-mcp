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

            var overflow = MaxOverflow(
                GetSelfOverflowAmounts(frameworkElement, frameworkElement.Clip, frameworkElement.ClipToBounds),
                GetAncestorOverflowAmounts(frameworkElement));
            var isClipped = frameworkElement.Clip != null || HasOverflow(overflow);
            checks.Add(CreateVisibilityCheck("clipping", isClipped ? "clipped" : "not-clipped", !isClipped, "Clipping"));

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
                isClipped,
                ancestors);

            return new
            {
                success = true,
                elementId = elementId ?? _elementFinder.GenerateElementId(frameworkElement),
                isUserVisible = diagnosis.isUserVisible,
                checks,
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
        bool isClipped,
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

        if (isClipped)
        {
            return (false,
                $"Element {elementId} is clipped by its own or an ancestor clipping region.",
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
}
