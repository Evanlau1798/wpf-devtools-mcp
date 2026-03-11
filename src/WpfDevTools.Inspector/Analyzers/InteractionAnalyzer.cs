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
                return ToolErrorFactory.OperationFailed(
                    "click element",
                    ex,
                    "Verify the element is enabled and still attached to the current visual tree before retrying.");
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
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target before calling scroll_to_element.");
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
                return ToolErrorFactory.OperationFailed(
                    "scroll to element",
                    ex,
                    "Ensure the target is inside a ScrollViewer and is still attached to the current visual tree.");
            }
        });
    }

    /// <summary>
    /// Take screenshot of element
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

            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

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
                        return ToolErrorFactory.ElementNotLoaded(
                            "Element has no visual bounds",
                            "Ensure the element is visible and rendered in the visual tree before taking a screenshot.");
                    }

                    bounds = new Rect(0, 0, renderSize.Width, renderSize.Height);
                }

                const int MaxDimensionPixels = 3840;
                if (bounds.Width > MaxDimensionPixels || bounds.Height > MaxDimensionPixels)
                {
                    return ToolErrorFactory.InvalidArgument(
                        $"Element too large to screenshot ({bounds.Width:F0}x{bounds.Height:F0} px). Maximum is {MaxDimensionPixels}x{MaxDimensionPixels}.",
                        "Target a smaller child element or reduce the rendered size before calling element_screenshot.");
                }

                var normalizedOutputMode = string.Equals(outputMode, "metadata", StringComparison.OrdinalIgnoreCase)
                    ? "metadata"
                    : "base64";
                var (targetWidth, targetHeight) = CalculateScaledDimensions(
                    bounds.Width,
                    bounds.Height,
                    maxWidth,
                    maxHeight);

                // Create render target
                var renderTarget = new RenderTargetBitmap(
                    targetWidth,
                    targetHeight,
                    96, 96,
                    PixelFormats.Pbgra32);

                // Render element
                var drawingVisual = new DrawingVisual();
                using (var context = drawingVisual.RenderOpen())
                {
                    var visualBrush = new VisualBrush(uiElement);
                    context.DrawRectangle(visualBrush, null, new Rect(0, 0, targetWidth, targetHeight));
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
                var imageBytes = stream.ToArray();

                if (normalizedOutputMode == "metadata")
                {
                    return new
                    {
                        success = true,
                        width = targetWidth,
                        height = targetHeight,
                        format = "png",
                        byteLength = imageBytes.Length
                    };
                }

                var base64Image = Convert.ToBase64String(imageBytes);

                return new
                {
                    success = true,
                    base64Image,
                    width = targetWidth,
                    height = targetHeight,
                    format = "png",
                    byteLength = imageBytes.Length
                };
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
                return ToolErrorFactory.OperationFailed(
                    "simulate drag and drop",
                    new NotSupportedException("Drag and drop simulation not supported on this .NET version"),
                    "This feature requires internal DragEventArgs reflection support that may be unavailable on the current runtime.");
            }

            var sourceElement = sourceElementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(sourceElementId);

            if (sourceElement == null)
            {
                return ToolErrorFactory.ElementNotFound(sourceElementId);
            }

            var targetElement = targetElementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(targetElementId);

            if (targetElement == null)
            {
                return ToolErrorFactory.ElementNotFound(targetElementId);
            }

            if (sourceElement is not UIElement sourceUI || targetElement is not UIElement targetUI)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Elements must be UIElement",
                    "Choose sourceElementId and targetElementId that resolve to UIElement instances before drag_and_drop.");
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
                    return ToolErrorFactory.OperationFailed(
                        "simulate drag and drop",
                        new NotSupportedException("DragEventArgs internal constructor not found in this .NET version"),
                        "This feature requires internal DragEventArgs reflection support that may be unavailable on the current runtime.");
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
                return ToolErrorFactory.OperationFailed(
                    "simulate drag and drop",
                    ex,
                    "Verify both elements still exist and support drag/drop semantics before retrying.");
            }
        });
    }

}
