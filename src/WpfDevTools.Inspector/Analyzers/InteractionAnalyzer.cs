using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes and simulates user interactions with WPF elements
/// </summary>
public sealed partial class InteractionAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;
    private readonly WatchEventBuffer? _watchEventBuffer;
    private readonly string? _screenshotDirectoryOverride;

    /// <summary>
    /// Create a new InteractionAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public InteractionAnalyzer(ElementFinder elementFinder)
        : this(elementFinder, null)
    {
    }

    internal InteractionAnalyzer(
        ElementFinder elementFinder,
        WatchEventBuffer? watchEventBuffer,
        string? screenshotDirectoryOverride = null)
        : base(elementFinder)
    {
        _elementFinder = elementFinder;
        _watchEventBuffer = watchEventBuffer;
        _screenshotDirectoryOverride = screenshotDirectoryOverride;
    }

    /// <summary>
    /// Click an element (Button, etc.)
    /// </summary>
    public object ClickElement(string? elementId)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            try
            {
                if (element is ButtonBase button)
                {
                    var readinessError = CreateClickReadinessError(button, elementId);
                    if (readinessError is not null)
                    {
                        return readinessError;
                    }

                    // OnClick() handles both RaiseEvent(ClickEvent) and Command execution.
                    // Do NOT call Command.Execute separately — it would double-execute.
                    ButtonBaseClickHelper.InvokeOnClick(button);
                    EnqueueRoutedEventRecord(
                        elementId ?? _elementFinder.GenerateElementId(button),
                        button,
                        ButtonBase.ClickEvent);

                    return new
                    {
                        success = true,
                        message = "Element clicked successfully",
                        elementType = element.GetType().Name
                    };
                }

                if (element is TabItem tabItem)
                {
                    var readinessError = CreateClickReadinessError(tabItem, elementId);
                    if (readinessError is not null)
                    {
                        return readinessError;
                    }

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

    private object? CreateClickReadinessError(FrameworkElement element, string? elementId)
    {
        var resolvedElementId = elementId ?? _elementFinder.GenerateElementId(element);
        var blockers = new List<object>();

        if (!element.IsEnabled)
        {
            blockers.Add(CreateBlocker("ElementDisabled", "Element IsEnabled is false."));
        }

        if (element.Visibility != Visibility.Visible)
        {
            blockers.Add(CreateBlocker("ElementHidden", $"Element Visibility is {element.Visibility}."));
        }

        if (element.Opacity <= 0)
        {
            blockers.Add(CreateBlocker("ElementTransparent", "Element Opacity is 0."));
        }

        if (!element.IsHitTestVisible)
        {
            blockers.Add(CreateBlocker("HitTestingDisabled", "Element IsHitTestVisible is false."));
        }

        if (element.IsLoaded && (element.ActualWidth <= 0 || element.ActualHeight <= 0))
        {
            var reason = SceneSummaryElementHelpers.GetLayoutSizeBlockerReason(element);
            blockers.Add(CreateBlocker(reason, "Element has zero ActualWidth or ActualHeight."));
        }

        var commandReadiness = CreateCommandReadiness(element, resolvedElementId, out var canExecute);
        if (canExecute == false)
        {
            blockers.Add(CreateBlocker("CommandCannotExecute", "The bound ICommand.CanExecute returned false."));
        }

        return blockers.Count == 0
            ? null
            : ToolErrorFactory.InteractionNotReady(
                resolvedElementId,
                element.GetType().Name,
                blockers,
                commandReadiness);
    }

    private void EnqueueRoutedEventRecord(string elementId, UIElement element, RoutedEvent routedEvent)
    {
        _watchEventBuffer?.Enqueue(new WatchEventRecord(
            EventType: "RoutedEvent",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: $"tool:routed:{elementId}:{routedEvent.Name}:{Guid.NewGuid():N}",
            ElementId: elementId,
            PropertyName: null,
            EventName: routedEvent.Name,
            NewValue: null,
            ValueType: null,
            SenderType: element.GetType().Name,
            SenderName: (element as FrameworkElement)?.Name,
            RoutingStrategy: routedEvent.RoutingStrategy.ToString(),
            Handled: null,
            OriginalSourceType: element.GetType().Name));
    }

    /// <summary>
    /// Scroll element into view
    /// </summary>
    public object ScrollToElement(string? elementId)
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

            var sourceElement = ResolveElement(sourceElementId);

            if (sourceElement == null)
            {
                return ToolErrorFactory.ElementNotFound(sourceElementId);
            }

            var targetElement = ResolveElement(targetElementId);

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
                var targetHandlerHints = CreateTargetHandlerHints(targetUI);

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

                var dragOverArgs = (DragEventArgs)constructor.Invoke(new object[]
                {
                    data,
                    DragDropKeyStates.None,
                    DragDropEffects.Copy,
                    targetUI,
                    new Point(0, 0)
                });
                dragOverArgs.RoutedEvent = DragDrop.DragOverEvent;
                targetUI.RaiseEvent(dragOverArgs);

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
                    dataFormat,
                    targetHandlerHints
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

    private static object CreateTargetHandlerHints(UIElement targetElement)
    {
        var targetAllowsDrop = targetElement.AllowDrop;
        if (!RoutedEventHandlerInspectionHelper.IsReflectionSupported())
        {
            return new
            {
                targetAllowsDrop,
                hasDropHandler = (bool?)null,
                hasDragOverHandler = (bool?)null,
                hasAnyDropOrDragOverHandler = (bool?)null,
                inspectionSupported = false,
                mayBeIncomplete = true
            };
        }

        var dropHandlerCount = RoutedEventHandlerInspectionHelper.GetHandlerInfos(targetElement, DragDrop.DropEvent).Count;
        var dragOverHandlerCount = RoutedEventHandlerInspectionHelper.GetHandlerInfos(targetElement, DragDrop.DragOverEvent).Count;

        return new
        {
            targetAllowsDrop,
            hasDropHandler = dropHandlerCount > 0,
            hasDragOverHandler = dragOverHandlerCount > 0,
            hasAnyDropOrDragOverHandler = dropHandlerCount > 0 || dragOverHandlerCount > 0,
            inspectionSupported = true,
            mayBeIncomplete = true
        };
    }

}
