using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class LayoutAnalyzer
{
    /// <summary>
    /// Get clipping information for an element and its visual ancestors.
    /// </summary>
    public object GetClippingInfo(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not UIElement uiElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a UIElement",
                    "Choose a UIElement target before calling get_clipping_info.");
            }

            var clip = uiElement.Clip;
            var effectiveClip = GetEffectiveClippingGeometry(uiElement);
            var selfOverflow = GetSelfOverflowAmounts(uiElement, effectiveClip, uiElement.ClipToBounds);
            var ancestorAnalysis = GetAncestorClippingAnalysis(uiElement);
            var overflow = MaxOverflow(selfOverflow, ancestorAnalysis.overflow);
            var selfSource = GetSelfClipSource(uiElement, effectiveClip, selfOverflow);
            var clippingSource = selfSource != "none"
                ? selfSource
                : ancestorAnalysis.primarySource;
            var isClipped = clip != null || HasOverflow(overflow);

            return new
            {
                success = true,
                isClipped,
                visibleContentImpact = isClipped ? "not-determined" : "none",
                analysisScope = "target-and-ancestors",
                clippingSource,
                clipToBounds = uiElement.ClipToBounds,
                hasClip = clip != null,
                hasEffectiveClip = effectiveClip != null,
                clipBounds = CreateBoundsInfo(clip?.Bounds),
                effectiveClipBounds = CreateBoundsInfo(effectiveClip?.Bounds),
                overflowAmount = CreateOverflowInfo(overflow),
                clippingAncestors = ancestorAnalysis.ancestors,
                suggestedFix = CreateClippingSuggestion(
                    isClipped,
                    selfSource,
                    ancestorAnalysis.primaryDisplay)
            };
        });
    }

    private (List<object> ancestors,
        (double left, double top, double right, double bottom) overflow,
        string primarySource,
        string? primaryDisplay) GetAncestorClippingAnalysis(UIElement element)
    {
        var ancestors = new List<object>();
        var overflow = (left: 0d, top: 0d, right: 0d, bottom: 0d);
        string? primarySource = null;
        string? primaryDisplay = null;

        if (element is not FrameworkElement frameworkElement)
        {
            return (ancestors, overflow, "none", null);
        }

        var elementBounds = GetContentBounds(frameworkElement);
        DependencyObject? current = VisualTreeHelper.GetParent(element);

        while (current is Visual ancestorVisual)
        {
            if (current is UIElement ancestorElement &&
                GetEffectiveClippingGeometry(ancestorElement) is Geometry effectiveClip)
            {
                try
                {
                    var transformedBounds = frameworkElement.TransformToAncestor(ancestorVisual)
                        .TransformBounds(elementBounds);
                    var ancestorOverflow = ComputeOverflow(transformedBounds, effectiveClip.Bounds);

                    if (HasOverflow(ancestorOverflow))
                    {
                        var clipSource = GetEffectiveClipSource(ancestorElement);
                        var elementType = ancestorElement.GetType().Name;
                        var elementName = (ancestorElement as FrameworkElement)?.Name;
                        var display = string.IsNullOrWhiteSpace(elementName)
                            ? elementType
                            : $"{elementType} '{elementName}'";

                        ancestors.Add(new
                        {
                            elementId = _elementFinder.GenerateElementId(ancestorElement),
                            elementType,
                            elementName = string.IsNullOrWhiteSpace(elementName) ? null : elementName,
                            clipSource,
                            effectiveClipBounds = CreateBoundsInfo(effectiveClip.Bounds),
                            overflowAmount = CreateOverflowInfo(ancestorOverflow)
                        });

                        primarySource ??= $"ancestor-{clipSource}";
                        primaryDisplay ??= display;
                        overflow = MaxOverflow(overflow, ancestorOverflow);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        if (TryGetWindowViewportBoundary(
                frameworkElement,
                out var viewportRoot,
                out var transformedContentBounds,
                out var viewportBounds))
        {
            var viewportOverflow = ComputeOverflow(transformedContentBounds, viewportBounds);
            if (HasOverflow(viewportOverflow))
            {
                var elementType = viewportRoot.GetType().Name;
                var elementName = (viewportRoot as FrameworkElement)?.Name;
                ancestors.Add(new
                {
                    elementId = _elementFinder.GenerateElementId(viewportRoot),
                    elementType,
                    elementName = string.IsNullOrWhiteSpace(elementName) ? null : elementName,
                    clipSource = "window-client-viewport",
                    effectiveClipBounds = CreateBoundsInfo(viewportBounds),
                    overflowAmount = CreateOverflowInfo(viewportOverflow)
                });
                primarySource ??= "window-client-viewport";
                primaryDisplay ??= "Window client viewport";
                overflow = MaxOverflow(overflow, viewportOverflow);
            }
        }

        return (ancestors, overflow, primarySource ?? "none", primaryDisplay);
    }

    private static bool TryGetWindowViewportBoundary(
        FrameworkElement element,
        out UIElement viewportRoot,
        out Rect transformedContentBounds,
        out Rect viewportBounds)
    {
        transformedContentBounds = Rect.Empty;
        viewportBounds = Rect.Empty;
        if (!TryGetVisibleViewportRoot(element, out viewportRoot)
            || viewportRoot.RenderSize.Width <= 0
            || viewportRoot.RenderSize.Height <= 0)
        {
            return false;
        }

        viewportBounds = new Rect(new Point(0, 0), viewportRoot.RenderSize);
        var contentBounds = GetContentBounds(element);
        if (contentBounds.IsEmpty)
        {
            return false;
        }

        if (ReferenceEquals(element, viewportRoot))
        {
            transformedContentBounds = contentBounds;
            return true;
        }

        try
        {
            transformedContentBounds = element.TransformToAncestor(viewportRoot)
                .TransformBounds(contentBounds);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static (double left, double top, double right, double bottom)
        GetSelfOverflowAmounts(UIElement element, Geometry? effectiveClip, bool clipToBounds)
    {
        var contentBounds = GetContentBounds(element);
        if (contentBounds.IsEmpty)
        {
            return (0d, 0d, 0d, 0d);
        }

        var overflow = (0d, 0d, 0d, 0d);
        if (effectiveClip != null)
        {
            overflow = MaxOverflow(overflow, ComputeOverflow(contentBounds, effectiveClip.Bounds));
        }

        if (clipToBounds)
        {
            var clipToBoundsRect = new Rect(new Point(0, 0), element.RenderSize);
            overflow = MaxOverflow(overflow, ComputeOverflow(contentBounds, clipToBoundsRect));
        }

        return overflow;
    }

    private static string GetSelfClipSource(
        UIElement element,
        Geometry? effectiveClip,
        (double left, double top, double right, double bottom) overflow)
    {
        if (element.Clip != null)
        {
            return "explicit-clip";
        }

        return HasOverflow(overflow) && (effectiveClip != null || element.ClipToBounds)
            ? GetEffectiveClipSource(element)
            : "none";
    }

    private static Geometry? GetEffectiveClippingGeometry(UIElement element)
    {
        return VisualTreeHelper.GetClip(element)
            ?? element.Clip
            ?? (element.ClipToBounds
                ? new RectangleGeometry(new Rect(new Point(0, 0), element.RenderSize))
                : null);
    }

    private static string GetEffectiveClipSource(UIElement element)
    {
        if (element.Clip != null)
        {
            return "explicit-clip";
        }

        return element.ClipToBounds ? "clip-to-bounds" : "layout-clip";
    }

    private static object? CreateBoundsInfo(Rect? bounds)
    {
        return bounds is Rect value
            ? new { x = value.X, y = value.Y, width = value.Width, height = value.Height }
            : null;
    }

    private static object CreateOverflowInfo(
        (double left, double top, double right, double bottom) overflow)
    {
        return new
        {
            left = overflow.left,
            top = overflow.top,
            right = overflow.right,
            bottom = overflow.bottom
        };
    }

    private static string? CreateClippingSuggestion(
        bool isClipped,
        string selfSource,
        string? ancestorDisplay)
    {
        if (!isClipped)
        {
            return null;
        }

        if (ancestorDisplay != null)
        {
            return $"Increase the available layout slot or reduce desired size under {ancestorDisplay}; " +
                   "inspect fixed sizes, margins, and Grid row or column sizing before changing clipping policy. " +
                   "This structural overflow is not proof of visible pixel loss; confirm affected content " +
                   "with focused descendant checks or a screenshot before changing layout.";
        }

        return selfSource == "explicit-clip"
            ? "Inspect the target's explicit Clip geometry and confirm that it contains the intended content."
            : "Increase the target's available layout slot or reduce its desired content size.";
    }

    private static Rect GetContentBounds(UIElement element)
    {
        var renderedBounds = new Rect(new Point(0, 0), element.RenderSize);
        var bounds = element is TextBlock textBlock
            ? Rect.Union(
                renderedBounds,
                new Rect(new Point(0, 0), MeasureTextContent(textBlock)))
            : renderedBounds;

        if (element is not Visual visual)
        {
            return bounds;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<UIElement>())
        {
            try
            {
                var childBounds = child.TransformToAncestor(visual).TransformBounds(GetContentBounds(child));
                bounds = bounds.IsEmpty ? childBounds : Rect.Union(bounds, childBounds);
            }
            catch (InvalidOperationException)
            {
            }
        }

        return bounds;
    }

    private static Size MeasureTextContent(TextBlock textBlock)
    {
        var typeface = new Typeface(
            textBlock.FontFamily,
            textBlock.FontStyle,
            textBlock.FontWeight,
            textBlock.FontStretch);

        var dpi = VisualTreeHelper.GetDpi(textBlock).PixelsPerDip;
        var formattedText = new FormattedText(
            textBlock.Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            textBlock.FlowDirection,
            typeface,
            textBlock.FontSize,
            textBlock.Foreground ?? Brushes.Black,
            dpi)
        {
            Trimming = textBlock.TextTrimming,
            TextAlignment = textBlock.TextAlignment
        };

        if (textBlock.LineHeight > 0)
        {
            formattedText.LineHeight = textBlock.LineHeight;
        }

        if (textBlock.TextWrapping != TextWrapping.NoWrap)
        {
            var wrapWidth = textBlock.ActualWidth > 0
                ? textBlock.ActualWidth
                : Math.Max(textBlock.RenderSize.Width, 0d);

            if (wrapWidth > 0)
            {
                formattedText.MaxTextWidth = wrapWidth;
            }
        }

        return new Size(
            Math.Max(textBlock.RenderSize.Width, formattedText.WidthIncludingTrailingWhitespace),
            Math.Max(textBlock.RenderSize.Height, formattedText.Height));
    }

    private static (double left, double top, double right, double bottom) ComputeOverflow(
        Rect elementBounds,
        Rect clippingBounds)
    {
        return (
            Math.Max(0d, clippingBounds.Left - elementBounds.Left),
            Math.Max(0d, clippingBounds.Top - elementBounds.Top),
            Math.Max(0d, elementBounds.Right - clippingBounds.Right),
            Math.Max(0d, elementBounds.Bottom - clippingBounds.Bottom));
    }

    private static bool HasOverflow((double left, double top, double right, double bottom) overflow)
    {
        return overflow.left > 0d || overflow.top > 0d || overflow.right > 0d || overflow.bottom > 0d;
    }

    private static (double left, double top, double right, double bottom) MaxOverflow(
        (double left, double top, double right, double bottom) first,
        (double left, double top, double right, double bottom) second)
    {
        return (
            Math.Max(first.left, second.left),
            Math.Max(first.top, second.top),
            Math.Max(first.right, second.right),
            Math.Max(first.bottom, second.bottom));
    }
}
