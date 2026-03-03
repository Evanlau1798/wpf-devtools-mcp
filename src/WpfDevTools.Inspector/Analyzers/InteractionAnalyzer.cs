using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes and simulates user interactions with WPF elements
/// </summary>
public class InteractionAnalyzer
{
    private readonly ElementFinder _elementFinder;

    public InteractionAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Click an element (Button, etc.)
    /// </summary>
    public object ClickElement(string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ClickElement(elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        try
        {
            if (element is ButtonBase button)
            {
                // Raise Click event directly
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

                return new
                {
                    success = true,
                    message = "Element clicked successfully",
                    elementType = element.GetType().Name
                };
            }

            return new
            {
                success = false,
                error = "Element is not clickable",
                elementType = element.GetType().Name
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to click element: {ex.Message}" };
        }
    }

    /// <summary>
    /// Scroll element into view
    /// </summary>
    public object ScrollToElement(string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ScrollToElement(elementId));
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
            fe.BringIntoView();

            return new
            {
                success = true,
                message = "Element scrolled into view",
                elementType = element.GetType().Name
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to scroll to element: {ex.Message}" };
        }
    }

    /// <summary>
    /// Take screenshot of element
    /// </summary>
    public object TakeScreenshot(string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => TakeScreenshot(elementId));
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
            // Get element bounds
            var bounds = VisualTreeHelper.GetDescendantBounds(uiElement);
            if (bounds.IsEmpty)
            {
                return new { success = false, error = "Element has no visual bounds" };
            }

            // Create render target
            var renderTarget = new RenderTargetBitmap(
                (int)bounds.Width,
                (int)bounds.Height,
                96, 96,
                PixelFormats.Pbgra32);

            // Render element
            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                var visualBrush = new VisualBrush(uiElement);
                context.DrawRectangle(visualBrush, null, bounds);
            }

            renderTarget.Render(drawingVisual);

            // Encode to PNG
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            var imageData = Convert.ToBase64String(stream.ToArray());

            return new
            {
                success = true,
                imageData,
                width = (int)bounds.Width,
                height = (int)bounds.Height,
                format = "png"
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to take screenshot: {ex.Message}" };
        }
    }
}
