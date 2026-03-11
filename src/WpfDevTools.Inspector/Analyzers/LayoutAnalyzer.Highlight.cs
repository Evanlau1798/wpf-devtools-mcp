using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class LayoutAnalyzer
{
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
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target before calling highlight_element.");
            }

            try
            {
                var brush = CreateHighlightBrush(color);

                var highlightBorder = new Border
                {
                    BorderBrush = brush,
                    BorderThickness = new Thickness(2),
                    IsHitTestVisible = false
                };

                var adornerLayer = AdornerLayer.GetAdornerLayer(fe);
                if (adornerLayer == null)
                {
                    return ToolErrorFactory.ElementNotLoaded(
                        "Cannot highlight element: AdornerLayer not available",
                        "Ensure the element is attached to a visual tree with an available AdornerLayer/AdornerDecorator ancestor before retrying highlight_element.");
                }

                var adorner = new HighlightAdorner(fe, highlightBorder);
                adornerLayer.Add(adorner);

                var key = elementId ?? "root";
                _highlights[key] = highlightBorder;

                Task.Delay(duration).ContinueWith(_ =>
                {
                    InvokeOnUIThread(() =>
                    {
                        adornerLayer.Remove(adorner);
                        _highlights.TryRemove(key, out Border? _);
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
                return ToolErrorFactory.OperationFailed(
                    "highlight element",
                    ex,
                    "Ensure the target is visible and attached to an AdornerLayer before retrying highlight_element.");
            }
        });
    }

    private static SolidColorBrush CreateHighlightBrush(string color)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LayoutAnalyzer: Invalid color '{color}', falling back to Red: {ex.Message}");
            return new SolidColorBrush(Colors.Red);
        }
    }

    private sealed class HighlightAdorner(UIElement adornedElement, Border border) : Adorner(adornedElement)
    {
        private readonly Border _border = border;

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(AdornedElement.RenderSize);
            drawingContext.DrawRectangle(null, new Pen(_border.BorderBrush, 2), rect);
        }
    }
}
