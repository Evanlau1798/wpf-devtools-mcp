using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Layout information
/// </summary>
public sealed partial class LayoutAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;
    private static readonly ConcurrentDictionary<string, Border> _highlights = new();

    /// <summary>
    /// Create a new LayoutAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public LayoutAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get layout information for an element
    /// </summary>
    public object GetLayoutInfo(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target from get_visual_tree or find_elements before inspecting layout.");
            }

            var notRenderedReason = GetNotRenderedReason(fe);

            return new
            {
                success = true,
                actualWidth = NormalizeDouble(fe.ActualWidth),
                actualHeight = NormalizeDouble(fe.ActualHeight),
                width = NormalizeDouble(fe.Width),
                height = NormalizeDouble(fe.Height),
                minWidth = NormalizeDouble(fe.MinWidth),
                minHeight = NormalizeDouble(fe.MinHeight),
                maxWidth = NormalizeDouble(fe.MaxWidth),
                maxHeight = NormalizeDouble(fe.MaxHeight),
                desiredSize = new
                {
                    width = NormalizeDouble(fe.DesiredSize.Width),
                    height = NormalizeDouble(fe.DesiredSize.Height)
                },
                desiredWidth = NormalizeDouble(fe.DesiredSize.Width),
                desiredHeight = NormalizeDouble(fe.DesiredSize.Height),
                renderSize = new
                {
                    width = NormalizeDouble(fe.RenderSize.Width),
                    height = NormalizeDouble(fe.RenderSize.Height)
                },
                margin = new
                {
                    left = NormalizeDouble(fe.Margin.Left),
                    top = NormalizeDouble(fe.Margin.Top),
                    right = NormalizeDouble(fe.Margin.Right),
                    bottom = NormalizeDouble(fe.Margin.Bottom)
                },
                padding = GetPaddingInfo(fe),
                horizontalAlignment = fe.HorizontalAlignment.ToString(),
                verticalAlignment = fe.VerticalAlignment.ToString(),
                layoutState = notRenderedReason == null ? "Rendered" : "NotRendered",
                notRenderedReason,
                positionInParent = GetPositionInfo(fe, VisualTreeHelper.GetParent(fe) as Visual),
                positionInWindow = GetPositionInfo(fe, Window.GetWindow(fe))
            };
        });
    }

    private static string? GetNotRenderedReason(FrameworkElement element)
    {
        return element.ActualWidth <= 0 || element.ActualHeight <= 0
            ? SceneSummaryElementHelpers.GetLayoutSizeBlockerReason(element)
            : null;
    }

    private static double? NormalizeDouble(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) ? value : null;
    }

    /// <summary>
    /// Get clipping information for an element
    /// </summary>
    public object GetClippingInfo(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

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
            var clipToBounds = uiElement.ClipToBounds;
            var overflow = MaxOverflow(
                GetSelfOverflowAmounts(uiElement, clip, clipToBounds),
                GetAncestorOverflowAmounts(uiElement));
            var isClipped = clip != null || HasOverflow(overflow);

            return new
            {
                success = true,
                isClipped = isClipped,
                clipToBounds = clipToBounds,
                hasClip = clip != null,
                clipBounds = clip != null ? new
                {
                    x = clip.Bounds.X,
                    y = clip.Bounds.Y,
                    width = clip.Bounds.Width,
                    height = clip.Bounds.Height
                } : null,
                overflowAmount = new
                {
                    left = overflow.left,
                    top = overflow.top,
                    right = overflow.right,
                    bottom = overflow.bottom
                }
            };
        });
    }

    private static object GetPaddingInfo(FrameworkElement element)
    {
        return element switch
        {
            Control control => CreateThicknessInfo(control.Padding),
            Border border => CreateThicknessInfo(border.Padding),
            _ => CreateThicknessInfo(null)
        };
    }

    private static object CreateThicknessInfo(Thickness? thickness)
    {
        return new
        {
            left = NormalizeDouble(thickness?.Left ?? double.NaN),
            top = NormalizeDouble(thickness?.Top ?? double.NaN),
            right = NormalizeDouble(thickness?.Right ?? double.NaN),
            bottom = NormalizeDouble(thickness?.Bottom ?? double.NaN)
        };
    }

    private static object GetPositionInfo(Visual target, Visual? relativeTo)
    {
        if (relativeTo == null)
        {
            return new { x = (double?)null, y = (double?)null };
        }

        try
        {
            var point = target.TransformToAncestor(relativeTo).Transform(new Point(0, 0));
            return new { x = NormalizeDouble(point.X), y = NormalizeDouble(point.Y) };
        }
        catch (InvalidOperationException)
        {
            return new { x = (double?)null, y = (double?)null };
        }
    }

    private static (double left, double top, double right, double bottom)
        GetSelfOverflowAmounts(UIElement element, Geometry? clip, bool clipToBounds)
    {
        var contentBounds = GetContentBounds(element);
        if (contentBounds.IsEmpty)
        {
            return (0d, 0d, 0d, 0d);
        }

        var overflow = (0d, 0d, 0d, 0d);
        if (clip != null)
        {
            overflow = MaxOverflow(overflow, ComputeOverflow(contentBounds, clip.Bounds));
        }

        if (clipToBounds)
        {
            var clipToBoundsRect = new Rect(new Point(0, 0), element.RenderSize);
            overflow = MaxOverflow(overflow, ComputeOverflow(contentBounds, clipToBoundsRect));
        }

        return overflow;
    }

    private static (double left, double top, double right, double bottom)
        GetAncestorOverflowAmounts(UIElement element)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return (0d, 0d, 0d, 0d);
        }

        var elementBounds = GetContentBounds(frameworkElement);
        var overflow = (left: 0d, top: 0d, right: 0d, bottom: 0d);
        DependencyObject? current = VisualTreeHelper.GetParent(element);

        while (current is Visual ancestorVisual)
        {
            Rect? clippingBounds = current switch
            {
                UIElement { Clip: not null } ancestorWithClip => ancestorWithClip.Clip!.Bounds,
                FrameworkElement { ClipToBounds: true } ancestorFramework => new Rect(new Point(0, 0), ancestorFramework.RenderSize),
                _ => null
            };

            if (clippingBounds != null)
            {
                try
                {
                    var transformedBounds = frameworkElement.TransformToAncestor(ancestorVisual).TransformBounds(elementBounds);
                    overflow = MaxOverflow(overflow, ComputeOverflow(transformedBounds, clippingBounds.Value));
                }
                catch (InvalidOperationException)
                {
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return overflow;
    }

    private static Rect GetContentBounds(UIElement element)
    {
        var bounds = GetElementContentBounds(element);

        if (element is not Visual visual)
        {
            return bounds;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(visual);
        for (var index = 0; index < childCount; index++)
        {
            if (VisualTreeHelper.GetChild(visual, index) is not UIElement child)
            {
                continue;
            }

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

    private static Rect GetElementContentBounds(UIElement element)
    {
        if (element is TextBlock textBlock)
        {
            return new Rect(new Point(0, 0), MeasureTextContent(textBlock));
        }

        var width = Math.Max(element.RenderSize.Width, element.DesiredSize.Width);
        var height = Math.Max(element.RenderSize.Height, element.DesiredSize.Height);
        return new Rect(new Point(0, 0), new Size(width, height));
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

    private static (double left, double top, double right, double bottom) ComputeOverflow(Rect elementBounds, Rect clippingBounds)
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

    /// <summary>
    /// Invalidate layout for an element
    /// </summary>
    public object InvalidateLayout(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not UIElement uiElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a UIElement",
                    "Choose a UIElement target before calling invalidate_layout.");
            }

            try
            {
                uiElement.InvalidateMeasure();
                uiElement.InvalidateArrange();
                uiElement.UpdateLayout();

                return new { success = true, message = "Layout invalidated and updated successfully" };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "invalidate layout",
                    ex,
                    "Ensure the target UIElement is still loaded before retrying invalidate_layout.");
            }
        });
    }
}
