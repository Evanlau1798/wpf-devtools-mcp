using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Layout information
/// </summary>
public class LayoutAnalyzer
{
    private readonly ElementFinder _elementFinder;
    private static readonly Dictionary<string, Border> _highlights = new();

    public LayoutAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get layout information for an element
    /// </summary>
    public object GetLayoutInfo(string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetLayoutInfo(elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        return new
        {
            actualWidth = fe.ActualWidth,
            actualHeight = fe.ActualHeight,
            width = fe.Width,
            height = fe.Height,
            minWidth = fe.MinWidth,
            minHeight = fe.MinHeight,
            maxWidth = fe.MaxWidth,
            maxHeight = fe.MaxHeight,
            desiredSize = new
            {
                width = fe.DesiredSize.Width,
                height = fe.DesiredSize.Height
            },
            renderSize = new
            {
                width = fe.RenderSize.Width,
                height = fe.RenderSize.Height
            },
            margin = new
            {
                left = fe.Margin.Left,
                top = fe.Margin.Top,
                right = fe.Margin.Right,
                bottom = fe.Margin.Bottom
            }
        };
    }

    /// <summary>
    /// Get clipping information for an element
    /// </summary>
    public object GetClippingInfo(string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetClippingInfo(elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not UIElement uiElement)
        {
            return new { error = "Element is not a UIElement" };
        }

        var clip = uiElement.Clip;
        var clipToBounds = uiElement.ClipToBounds;

        return new
        {
            clipToBounds = clipToBounds,
            hasClip = clip != null,
            clipBounds = clip != null ? new
            {
                x = clip.Bounds.X,
                y = clip.Bounds.Y,
                width = clip.Bounds.Width,
                height = clip.Bounds.Height
            } : null
        };
    }

    /// <summary>
    /// Invalidate layout for an element
    /// </summary>
    public object InvalidateLayout(string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => InvalidateLayout(elementId));
        }

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
    }

    /// <summary>
    /// Highlight an element with a colored border overlay
    /// </summary>
    public object HighlightElement(string? elementId, string color, int duration)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                HighlightElement(elementId, color, duration));
        }

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
            // Parse color
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

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
                return new { success = false, error = "Cannot get AdornerLayer for element" };
            }

            // Create adorner
            var adorner = new HighlightAdorner(fe, highlightBorder);
            adornerLayer.Add(adorner);

            // Store reference
            var key = elementId ?? "root";
            _highlights[key] = highlightBorder;

            // Remove highlight after duration
            Task.Delay(duration).ContinueWith(_ =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    adornerLayer.Remove(adorner);
                    _highlights.Remove(key);
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
