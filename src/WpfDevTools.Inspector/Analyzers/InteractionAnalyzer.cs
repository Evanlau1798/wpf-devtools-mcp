using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes and simulates user interactions with WPF elements
/// </summary>
public sealed partial class InteractionAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    /// <summary>
    /// Create a new InteractionAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public InteractionAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Click an element (Button, etc.)
    /// </summary>
    public object ClickElement(string? elementId)
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

            try
            {
                if (element is ButtonBase button)
                {
                    // OnClick() handles both RaiseEvent(ClickEvent) and Command execution.
                    // Do NOT call Command.Execute separately — it would double-execute.
                    ButtonBaseClickHelper.InvokeOnClick(button);

                    return new
                    {
                        success = true,
                        message = "Element clicked successfully",
                        elementType = element.GetType().Name
                    };
                }

                if (element is TabItem tabItem)
                {
                    tabItem.IsSelected = true;
                    tabItem.Focus();

                    return new
                    {
                        success = true,
                        message = "Tab selected successfully",
                        elementType = element.GetType().Name
                    };
                }

                return ToolErrorFactory.ElementNotClickable(element.GetType().Name);
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Failed to click element: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Scroll element into view
    /// </summary>
    public object ScrollToElement(string? elementId)
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
        });
    }

    /// <summary>
    /// Take screenshot of element
    /// </summary>
    public object TakeScreenshot(string? elementId)
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
                // Get element bounds
                var bounds = VisualTreeHelper.GetDescendantBounds(uiElement);
                if (bounds.IsEmpty)
                {
                    var renderSize = uiElement.RenderSize;
                    if ((renderSize.Width <= 0 || renderSize.Height <= 0) && uiElement is FrameworkElement frameworkElement)
                    {
                        var fallbackWidth = frameworkElement.Width;
                        var fallbackHeight = frameworkElement.Height;
                        if (!double.IsNaN(fallbackWidth) && fallbackWidth > 0 && !double.IsNaN(fallbackHeight) && fallbackHeight > 0)
                        {
                            frameworkElement.Measure(new Size(fallbackWidth, fallbackHeight));
                            frameworkElement.Arrange(new Rect(0, 0, fallbackWidth, fallbackHeight));
                            frameworkElement.UpdateLayout();
                            renderSize = frameworkElement.RenderSize;
                        }
                    }

                    if (renderSize.Width <= 0 || renderSize.Height <= 0)
                    {
                        return new { success = false, error = "Element has no visual bounds" };
                    }

                    bounds = new Rect(0, 0, renderSize.Width, renderSize.Height);
                }

                const int MaxDimensionPixels = 3840;
                if (bounds.Width > MaxDimensionPixels || bounds.Height > MaxDimensionPixels)
                {
                    return new { success = false, error = $"Element too large to screenshot ({bounds.Width:F0}x{bounds.Height:F0} px). Maximum is {MaxDimensionPixels}x{MaxDimensionPixels}." };
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

                // Render adorner layer on top (for highlight overlays, etc.)
                try
                {
                    var adornerLayer = AdornerLayer.GetAdornerLayer(uiElement);
                    if (adornerLayer != null)
                    {
                        var adorners = adornerLayer.GetAdorners(uiElement);
                        if (adorners != null && adorners.Length > 0)
                        {
                            var adornerVisual = new DrawingVisual();
                            using (var adornerContext = adornerVisual.RenderOpen())
                            {
                                var adornerBrush = new VisualBrush(adornerLayer);
                                adornerContext.DrawRectangle(adornerBrush, null, bounds);
                            }
                            renderTarget.Render(adornerVisual);
                        }
                    }
                }
                catch
                {
                    // Adorner capture is best-effort; skip if unavailable
                }

                // Encode to PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderTarget));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                var base64Image = Convert.ToBase64String(stream.ToArray());

                return new
                {
                    success = true,
                    base64Image,
                    width = (int)bounds.Width,
                    height = (int)bounds.Height,
                    format = "png"
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Failed to take screenshot: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Simulate drag and drop operation between elements
    /// </summary>
    public object DragAndDrop(string? sourceElementId, string? targetElementId, string dataFormat)
    {
        return InvokeOnUIThread<object>(() =>
        {
            // Check reflection support on first use
            if (!InteractionDragDropHelper.IsReflectionSupported())
            {
                return new
                {
                    success = false,
                    error = "Drag and drop simulation not supported on this .NET version",
                    note = "This feature requires access to internal DragEventArgs constructor that may not be available"
                };
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
                var originalTargetText = targetElement is TextBox targetTextBox
                    ? targetTextBox.Text
                    : null;

                // Create drag data
                var data = InteractionDragDropHelper.CreateDataObject(sourceElement, dataFormat);

                // Use reflection to create DragEventArgs (constructor is internal)
                var dragEventArgsType = typeof(DragEventArgs);
                var constructor = dragEventArgsType.GetConstructor(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new[] { typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point) },
                    null);

                if (constructor == null)
                {
                    InteractionDragDropHelper.MarkReflectionUnsupported();

                    return new
                    {
                        success = false,
                        error = "Drag and drop simulation not available",
                        note = "DragEventArgs internal constructor not found in this .NET version"
                    };
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

                InteractionDragDropHelper.NormalizeTextDropResult(
                    sourceElement,
                    targetElement,
                    dataFormat,
                    originalTargetText);

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
        });
    }

}
