using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Utilities;

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
            var element = ResolveElement(elementId);

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
                var highlightBorder = CreateHighlightBorder(brush);
                var key = elementId ?? "root";
                var createdAtUtc = DateTimeOffset.UtcNow;
                var effectiveDuration = GetEffectiveHighlightDuration(duration);
                var effectiveDurationMs = (int)effectiveDuration.TotalMilliseconds;
                var expiresAtUtc = createdAtUtc.Add(effectiveDuration);

                var adornerLayer = AdornerLayer.GetAdornerLayer(fe);
                Action removeHighlight;
                string highlightSurface;

                if (adornerLayer != null)
                {
                    var adorner = new HighlightAdorner(fe, highlightBorder);
                    adornerLayer.Add(adorner);
                    removeHighlight = CreateHighlightRemoval(adornerLayer, adorner);
                    highlightSurface = "adorner";
                }
                else if (TryCreatePopupHighlight(fe, highlightBorder, out removeHighlight))
                {
                    highlightSurface = "popup";
                }
                else
                {
                    return ToolErrorFactory.ElementNotLoaded(
                        "Cannot highlight element: target has no rendered size",
                        "Ensure the target element is visible, loaded, and has non-zero rendered bounds before retrying highlight_element.");
                }

                var entry = new HighlightEntry(
                    createdAtUtc,
                    expiresAtUtc,
                    removeHighlight);
                RegisterHighlight(key, entry);
                ScheduleHighlightRemoval(key, entry, effectiveDuration);

                return new
                {
                    success = true,
                    message = $"Element highlighted with {color} for {effectiveDurationMs}ms",
                    color,
                    duration = effectiveDurationMs,
                    requestedDuration = duration,
                    effectiveDuration = effectiveDurationMs,
                    durationCapped = effectiveDurationMs != Math.Max(0, duration),
                    elementType = element.GetType().Name,
                    highlightSurface
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "highlight element",
                    ex,
                    "Ensure the target is visible and loaded before retrying highlight_element.");
            }
        });
    }

    private static Border CreateHighlightBorder(Brush brush)
    {
        return new Border
        {
            BorderBrush = brush,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
    }

    private static SolidColorBrush CreateHighlightBrush(string color)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"LayoutAnalyzer: Invalid color '{SensitiveLogRedactor.Redact(color)}', falling back to Red: {SensitiveLogRedactor.Redact(ex.Message)}");
            return new SolidColorBrush(Colors.Red);
        }
    }

    private static bool TryCreatePopupHighlight(
        FrameworkElement target,
        Border highlightBorder,
        out Action removeHighlight)
    {
        removeHighlight = null!;
        var placementTarget = GetPopupPlacementTarget(target);
        var highlightSize = GetHighlightSize(placementTarget);
        var width = highlightSize.Width;
        var height = highlightSize.Height;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        highlightBorder.Width = width;
        highlightBorder.Height = height;

        var popup = new Popup
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            StaysOpen = true,
            IsHitTestVisible = false,
            Focusable = false,
            HorizontalOffset = 0,
            VerticalOffset = 0,
            Child = highlightBorder
        };

        popup.IsOpen = true;
        removeHighlight = CreateHighlightRemoval(popup);
        return true;
    }

    private static FrameworkElement GetPopupPlacementTarget(FrameworkElement target)
    {
        if (HasHighlightBounds(target))
        {
            return target;
        }

        return target is Window { Content: FrameworkElement content } && HasHighlightBounds(content)
            ? content
            : target;
    }

    private static bool HasHighlightBounds(FrameworkElement element)
    {
        var highlightSize = GetHighlightSize(element);
        return highlightSize.Width > 0 && highlightSize.Height > 0;
    }

    private static Size GetHighlightSize(FrameworkElement element)
    {
        return new Size(
            GetHighlightDimension(element.ActualWidth, element.RenderSize.Width, element.Width, element.DesiredSize.Width),
            GetHighlightDimension(element.ActualHeight, element.RenderSize.Height, element.Height, element.DesiredSize.Height));
    }

    private static double GetHighlightDimension(
        double actual,
        double render,
        double explicitLength,
        double desired)
    {
        if (actual > 0)
        {
            return actual;
        }

        if (render > 0)
        {
            return render;
        }

        if (!double.IsNaN(explicitLength) && !double.IsInfinity(explicitLength) && explicitLength > 0)
        {
            return explicitLength;
        }

        return desired > 0 ? desired : 0;
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
