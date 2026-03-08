using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Layout information
/// </summary>
public sealed class LayoutAnalyzer : DispatcherAnalyzerBase
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
                return new { success = false, error = "Element not found" };
            }

            if (element is not FrameworkElement fe)
            {
                return new { success = false, error = "Element is not a FrameworkElement" };
            }

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
                positionInParent = GetPositionInfo(fe, VisualTreeHelper.GetParent(fe) as Visual),
                positionInWindow = GetPositionInfo(fe, Window.GetWindow(fe))
            };
        });
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
                return new { success = false, error = "Element not found" };
            }

            if (element is not UIElement uiElement)
            {
                return new { success = false, error = "Element is not a UIElement" };
            }

            var clip = uiElement.Clip;
            var clipToBounds = uiElement.ClipToBounds;
            var overflow = GetOverflowAmounts(uiElement, clip);
            var isClipped = clip != null ||
                overflow.left > 0d ||
                overflow.top > 0d ||
                overflow.right > 0d ||
                overflow.bottom > 0d;

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
        GetOverflowAmounts(UIElement element, Geometry? clip)
    {
        if (element is not FrameworkElement frameworkElement || clip == null)
        {
            return (0d, 0d, 0d, 0d);
        }

        var bounds = clip.Bounds;
        return (
            Math.Max(0d, -bounds.Left),
            Math.Max(0d, -bounds.Top),
            Math.Max(0d, frameworkElement.RenderSize.Width - bounds.Right),
            Math.Max(0d, frameworkElement.RenderSize.Height - bounds.Bottom));
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
                return new { success = false, error = "Element not found" };
            }

            if (element is not UIElement uiElement)
            {
                return new { success = false, error = "Element is not a UIElement" };
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
                return new { success = false, error = $"Failed to invalidate layout: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Highlight an element with a colored border overlay
    /// </summary>
    public object HighlightElement(string? elementId, string color, int duration)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return new { success = false, error = "Element not found" };
            }

            if (element is not FrameworkElement fe)
            {
                return new { success = false, error = "Element is not a FrameworkElement" };
            }

            try
            {
                // Parse color with fallback
                Color parsedColor;
                try
                {
                    parsedColor = (Color)ColorConverter.ConvertFromString(color);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LayoutAnalyzer: Invalid color '{color}', falling back to Red: {ex.Message}");
                    parsedColor = Colors.Red;
                }
                var brush = new SolidColorBrush(parsedColor);

                // Create highlight border
                var highlightBorder = new Border
                {
                    BorderBrush = brush,
                    BorderThickness = new Thickness(2),
                    IsHitTestVisible = false
                };

                // Get adorner layer
                var adornerLayer = AdornerLayer.GetAdornerLayer(fe);
                if (adornerLayer == null)
                {
                    return new
                    {
                        success = false,
                        error = "Cannot highlight element: AdornerLayer not available",
                        hint = "Element may not be in the visual tree or may not have an AdornerDecorator ancestor"
                    };
                }

                // Create adorner
                var adorner = new HighlightAdorner(fe, highlightBorder);
                adornerLayer.Add(adorner);

                // Store reference
                var key = elementId ?? "root";
                _highlights[key] = highlightBorder;

                // Remove highlight after duration
                Task.Delay(duration).ContinueWith(task =>
                {
                    InvokeOnUIThread(() =>
                    {
                        adornerLayer.Remove(adorner);
                        _highlights.TryRemove(key, out _);
                    });
                });

                return new
                {
                    success = true,
                    message = $"Element highlighted with {color} for {duration}ms",
                    color,
                    duration,
                    elementType = element.GetType().Name
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Failed to highlight element: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Helper adorner class for highlighting elements
    /// </summary>
    private class HighlightAdorner : Adorner
    {
        private readonly Border _border;

        public HighlightAdorner(UIElement adornedElement, Border border) : base(adornedElement)
        {
            _border = border;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(AdornedElement.RenderSize);
            drawingContext.DrawRectangle(null, new Pen(_border.BorderBrush, 2), rect);
        }
    }
}
