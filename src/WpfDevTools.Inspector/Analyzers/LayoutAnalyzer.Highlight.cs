using System.Windows;
using System.Windows.Controls;
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
                var createdAtUtc = DateTimeOffset.UtcNow;
                var effectiveDuration = GetEffectiveHighlightDuration(duration);
                var effectiveDurationMs = (int)effectiveDuration.TotalMilliseconds;
                var expiresAtUtc = createdAtUtc.Add(effectiveDuration);
                var entry = new HighlightEntry(
                    createdAtUtc,
                    expiresAtUtc,
                    CreateHighlightRemoval(adornerLayer, adorner));
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
            System.Diagnostics.Debug.WriteLine(
                $"LayoutAnalyzer: Invalid color '{SensitiveLogRedactor.Redact(color)}', falling back to Red: {SensitiveLogRedactor.Redact(ex.Message)}");
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
