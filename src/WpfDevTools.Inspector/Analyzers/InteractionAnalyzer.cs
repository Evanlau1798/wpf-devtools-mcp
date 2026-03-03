using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Input;
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

    /// <summary>
    /// Simulate drag and drop operation between elements
    /// </summary>
    public object DragAndDrop(string? sourceElementId, string? targetElementId, string dataFormat)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                DragAndDrop(sourceElementId, targetElementId, dataFormat));
        }

        var sourceElement = sourceElementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(sourceElementId);

        if (sourceElement == null)
        {
            return new { success = false, error = "Source element not found" };
        }

        var targetElement = targetElementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(targetElementId);

        if (targetElement == null)
        {
            return new { success = false, error = "Target element not found" };
        }

        if (sourceElement is not UIElement sourceUI || targetElement is not UIElement targetUI)
        {
            return new { success = false, error = "Elements must be UIElement" };
        }

        try
        {
            // Create drag data
            var data = new DataObject(dataFormat, sourceElement);

            // Use reflection to create DragEventArgs (constructor is internal)
            var dragEventArgsType = typeof(DragEventArgs);
            var constructor = dragEventArgsType.GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point) },
                null);

            if (constructor == null)
            {
                return new { success = false, error = "Cannot access DragEventArgs constructor" };
            }

            // Simulate drag enter
            var dragEnterArgs = (DragEventArgs)constructor.Invoke(new object[]
            {
                data,
                DragDropKeyStates.None,
                DragDropEffects.Copy,
                targetUI,
                new Point(0, 0)
            });
            dragEnterArgs.RoutedEvent = DragDrop.DragEnterEvent;
            targetUI.RaiseEvent(dragEnterArgs);

            // Simulate drop
            var dropArgs = (DragEventArgs)constructor.Invoke(new object[]
            {
                data,
                DragDropKeyStates.None,
                DragDropEffects.Copy,
                targetUI,
                new Point(0, 0)
            });
            dropArgs.RoutedEvent = DragDrop.DropEvent;
            targetUI.RaiseEvent(dropArgs);

            return new
            {
                success = true,
                message = "Drag and drop simulated successfully",
                sourceType = sourceElement.GetType().Name,
                targetType = targetElement.GetType().Name,
                dataFormat
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to simulate drag and drop: {ex.Message}" };
        }
    }

    /// <summary>
    /// Simulate keyboard input to element
    /// </summary>
    public object SimulateKeyboard(string? elementId, string key, string eventType)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                SimulateKeyboard(elementId, key, eventType));
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
            // Parse key
            if (!Enum.TryParse<Key>(key, out var parsedKey))
            {
                return new { success = false, error = $"Invalid key: {key}" };
            }

            // Create keyboard event
            RoutedEvent routedEvent;
            if (eventType?.ToLower() == "keydown")
            {
                routedEvent = Keyboard.KeyDownEvent;
            }
            else if (eventType?.ToLower() == "keyup")
            {
                routedEvent = Keyboard.KeyUpEvent;
            }
            else
            {
                return new { success = false, error = "Invalid event type. Use 'KeyDown' or 'KeyUp'" };
            }

            var keyEventArgs = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(uiElement),
                0,
                parsedKey)
            {
                RoutedEvent = routedEvent
            };

            uiElement.RaiseEvent(keyEventArgs);

            return new
            {
                success = true,
                message = $"Keyboard event '{eventType}' simulated for key '{key}'",
                key,
                eventType,
                elementType = element.GetType().Name
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to simulate keyboard: {ex.Message}" };
        }
    }
}
