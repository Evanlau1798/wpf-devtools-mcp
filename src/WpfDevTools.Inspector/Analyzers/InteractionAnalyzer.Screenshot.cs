using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class InteractionAnalyzer
{
    /// <summary>
    /// Take screenshot of element.
    /// </summary>
    public object TakeScreenshot(
        string? elementId,
        string? outputMode = null,
        int? maxWidth = null,
        int? maxHeight = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (maxWidth.HasValue && maxWidth.Value <= 0)
            {
                return ToolErrorFactory.InvalidArgument(
                    "maxWidth must be a positive integer when provided",
                    "Provide a positive maxWidth value, or omit it to keep the rendered width.");
            }

            if (maxHeight.HasValue && maxHeight.Value <= 0)
            {
                return ToolErrorFactory.InvalidArgument(
                    "maxHeight must be a positive integer when provided",
                    "Provide a positive maxHeight value, or omit it to keep the rendered height.");
            }

            var element = ResolveElement(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not UIElement uiElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a UIElement",
                    "Choose a UIElement target from get_visual_tree before taking a screenshot.");
            }

            try
            {
                var bounds = GetScreenshotBounds(uiElement);
                var normalizedOutputMode = NormalizeOutputMode(outputMode);
                if (normalizedOutputMode == null)
                {
                    return ToolErrorFactory.InvalidArgument(
                        $"Unsupported outputMode '{outputMode}'",
                        "Use outputMode 'base64', 'metadata', or 'file'.");
                }

                var (targetWidth, targetHeight) = CalculateScaledDimensions(
                    bounds.Width,
                    bounds.Height,
                    maxWidth,
                    maxHeight);

                if (normalizedOutputMode == "metadata")
                {
                    return new
                    {
                        success = true,
                        width = targetWidth,
                        height = targetHeight,
                        format = "png",
                        rendered = false,
                        byteLength = 0
                    };
                }

                var budgetError = ValidateRenderedBudget(targetWidth, targetHeight, bounds.Width, bounds.Height);
                if (budgetError is not null)
                {
                    return budgetError;
                }

                var imageBytes = RenderScreenshotBytes(uiElement, bounds, targetWidth, targetHeight);
                if (imageBytes.Length > ScreenshotStorage.MaxEncodedPngBytes)
                {
                    return ToolErrorFactory.PayloadTooLarge(
                        $"Screenshot PNG payload is {imageBytes.Length} bytes, exceeding the {ScreenshotStorage.MaxEncodedPngBytes} byte limit.",
                        "Use outputMode 'metadata', target a smaller element, or provide smaller maxWidth/maxHeight values.",
                        new
                        {
                            byteLength = imageBytes.Length,
                            maxByteLength = ScreenshotStorage.MaxEncodedPngBytes,
                            width = targetWidth,
                            height = targetHeight
                        });
                }

                if (normalizedOutputMode == "file")
                {
                    var screenshot = ScreenshotStorage.WritePng(imageBytes, _screenshotDirectoryOverride);
                    return new
                    {
                        success = true,
                        screenshotId = screenshot.ScreenshotId,
                        path = screenshot.Path,
                        sha256 = screenshot.Sha256,
                        width = targetWidth,
                        height = targetHeight,
                        format = "png",
                        rendered = true,
                        byteLength = imageBytes.Length
                    };
                }

                return new
                {
                    success = true,
                    base64Image = Convert.ToBase64String(imageBytes),
                    width = targetWidth,
                    height = targetHeight,
                    format = "png",
                    rendered = true,
                    byteLength = imageBytes.Length
                };
            }
            catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "targetSize")
            {
                return ToolErrorFactory.InvalidArgument(
                    ex.Message,
                    "Target a smaller child element or reduce the rendered size before calling element_screenshot.");
            }
            catch (InvalidOperationException ex) when (ex.Message == "Element has no visual bounds")
            {
                return ToolErrorFactory.ElementNotLoaded(
                    ex.Message,
                    "Ensure the element is visible and rendered in the visual tree before taking a screenshot.");
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "take screenshot",
                    ex,
                    "Scroll the element into view and ensure it is rendered before retrying element_screenshot.");
            }
        });
    }

    private static Rect GetScreenshotBounds(UIElement uiElement)
    {
        var bounds = VisualTreeHelper.GetDescendantBounds(uiElement);
        if (!bounds.IsEmpty)
        {
            return bounds;
        }

        var renderSize = uiElement.RenderSize;
        if ((renderSize.Width <= 0 || renderSize.Height <= 0) && uiElement is FrameworkElement frameworkElement)
        {
            var fallbackWidth = frameworkElement.Width;
            var fallbackHeight = frameworkElement.Height;
            if (!double.IsNaN(fallbackWidth) && fallbackWidth > 0 &&
                !double.IsNaN(fallbackHeight) && fallbackHeight > 0)
            {
                frameworkElement.Measure(new Size(fallbackWidth, fallbackHeight));
                frameworkElement.Arrange(new Rect(0, 0, fallbackWidth, fallbackHeight));
                frameworkElement.UpdateLayout();
                renderSize = frameworkElement.RenderSize;
            }
        }

        if (renderSize.Width <= 0 || renderSize.Height <= 0)
        {
            throw new InvalidOperationException("Element has no visual bounds");
        }

        bounds = new Rect(0, 0, renderSize.Width, renderSize.Height);
        return bounds;
    }

    private static byte[] RenderScreenshotBytes(
        UIElement uiElement,
        Rect bounds,
        int targetWidth,
        int targetHeight)
    {
        var renderTarget = new RenderTargetBitmap(
            targetWidth,
            targetHeight,
            96,
            96,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            var visualBrush = new VisualBrush(uiElement);
            context.DrawRectangle(visualBrush, null, new Rect(0, 0, targetWidth, targetHeight));
        }

        renderTarget.Render(drawingVisual);
        TryRenderAdornerLayer(uiElement, renderTarget, bounds);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void TryRenderAdornerLayer(UIElement uiElement, RenderTargetBitmap renderTarget, Rect bounds)
    {
        try
        {
            var adornerLayer = AdornerLayer.GetAdornerLayer(uiElement);
            if (adornerLayer == null)
            {
                return;
            }

            var adorners = adornerLayer.GetAdorners(uiElement);
            if (adorners == null || adorners.Length == 0)
            {
                return;
            }

            var adornerVisual = new DrawingVisual();
            using var adornerContext = adornerVisual.RenderOpen();
            var adornerBrush = new VisualBrush(adornerLayer);
            adornerContext.DrawRectangle(adornerBrush, null, bounds);
            renderTarget.Render(adornerVisual);
        }
        catch
        {
            // Adorner capture is best-effort; skip if unavailable
        }
    }

    private static string? NormalizeOutputMode(string? outputMode)
    {
        if (string.IsNullOrWhiteSpace(outputMode))
        {
            return "metadata";
        }

        if (string.Equals(outputMode, "metadata", StringComparison.OrdinalIgnoreCase))
        {
            return "metadata";
        }

        if (string.Equals(outputMode, "base64", StringComparison.OrdinalIgnoreCase))
        {
            return "base64";
        }

        if (string.Equals(outputMode, "file", StringComparison.OrdinalIgnoreCase))
        {
            return "file";
        }

        return null;
    }

    private static object? ValidateRenderedBudget(
        int targetWidth,
        int targetHeight,
        double originalWidth,
        double originalHeight)
    {
        const int maxDimensionPixels = 3840;
        if (targetWidth > maxDimensionPixels || targetHeight > maxDimensionPixels)
        {
            return ToolErrorFactory.PayloadTooLarge(
                $"Element too large to screenshot ({originalWidth:F0}x{originalHeight:F0} px; rendered {targetWidth}x{targetHeight} px). Maximum rendered dimension is {maxDimensionPixels}px.",
                "Use outputMode 'metadata', target a smaller element, or provide smaller maxWidth/maxHeight values.",
                new
                {
                    width = targetWidth,
                    height = targetHeight,
                    maxDimensionPixels
                });
        }

        const long maxRenderedBytes = 32L * 1024 * 1024;
        var estimatedRenderedBytes = (long)targetWidth * targetHeight * 4;
        if (estimatedRenderedBytes > maxRenderedBytes)
        {
            return ToolErrorFactory.PayloadTooLarge(
                $"Screenshot render buffer would require {estimatedRenderedBytes} bytes, exceeding the {maxRenderedBytes} byte limit.",
                "Use outputMode 'metadata', target a smaller element, or provide smaller maxWidth/maxHeight values.",
                new
                {
                    width = targetWidth,
                    height = targetHeight,
                    estimatedRenderedBytes,
                    maxRenderedBytes
                });
        }

        return null;
    }

    private static (int Width, int Height) CalculateScaledDimensions(
        double originalWidth,
        double originalHeight,
        int? maxWidth,
        int? maxHeight)
    {
        var scale = 1.0;

        if (maxWidth.HasValue)
        {
            scale = Math.Min(scale, maxWidth.Value / originalWidth);
        }

        if (maxHeight.HasValue)
        {
            scale = Math.Min(scale, maxHeight.Value / originalHeight);
        }

        scale = Math.Min(scale, 1.0);

        var width = Math.Max(1, (int)Math.Round(originalWidth * scale));
        var height = Math.Max(1, (int)Math.Round(originalHeight * scale));
        return (width, height);
    }
}
