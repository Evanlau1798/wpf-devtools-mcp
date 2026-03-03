using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Helper for capturing screenshots of WPF elements
/// </summary>
public class ScreenshotHelper
{
    /// <summary>
    /// Capture screenshot of an element
    /// </summary>
    public object CaptureElement(string? elementId = null, string? outputPath = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => CaptureElement(elementId, outputPath));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

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
            // Get element size
            var size = new Size(
                (element as FrameworkElement)?.ActualWidth ?? 0,
                (element as FrameworkElement)?.ActualHeight ?? 0
            );

            if (size.Width == 0 || size.Height == 0)
            {
                return new { success = false, error = "Element has zero size" };
            }

            // Create RenderTargetBitmap
            var renderBitmap = new RenderTargetBitmap(
                (int)size.Width,
                (int)size.Height,
                96, // DPI X
                96, // DPI Y
                PixelFormats.Pbgra32
            );

            // Render element
            renderBitmap.Render(uiElement);

            // Encode to PNG
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

            // Save or return base64
            if (!string.IsNullOrEmpty(outputPath))
            {
                using var fileStream = new FileStream(outputPath, FileMode.Create);
                encoder.Save(fileStream);
                return new { success = true, message = $"Screenshot saved to {outputPath}", path = outputPath };
            }
            else
            {
                using var memoryStream = new MemoryStream();
                encoder.Save(memoryStream);
                var base64 = Convert.ToBase64String(memoryStream.ToArray());
                return new { success = true, message = "Screenshot captured", base64 = base64 };
            }
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to capture screenshot: {ex.Message}" };
        }
    }

    private DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    private DependencyObject? FindElementById(string elementId)
    {
        // TODO: Implement element lookup by ID
        return GetRootElement();
    }
}
