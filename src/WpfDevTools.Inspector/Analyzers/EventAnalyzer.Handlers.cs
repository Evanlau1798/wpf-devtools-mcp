using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class EventAnalyzer
{
    /// <summary>
    /// Get event handlers attached to an element
    /// </summary>
    public object GetEventHandlers(string? elementId, string eventName)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return ToolErrorFactory.InvalidArgument(
                    "eventName is required",
                    "Provide a routed event name such as Click, MouseDown, or PreviewMouseDown.");
            }

            if (!RoutedEventHandlerInspectionHelper.IsReflectionSupported())
            {
                return ToolErrorFactory.OperationFailed(
                    "get event handlers",
                    new NotSupportedException("Event handler inspection is not supported on this .NET version"),
                    "This feature requires internal WPF event store reflection support that may be unavailable on the current runtime.");
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
                    "Choose a UIElement target from get_visual_tree before inspecting event handlers.");
            }

            var routedEvent = FindRoutedEvent(uiElement, eventName);
            if (routedEvent == null)
            {
                var availableEvents = RoutedEventDiscovery.EnumerateAvailableRoutedEvents(uiElement.GetType());
                return ToolErrorFactory.EventNotFound(eventName, availableEvents);
            }

            try
            {
                var handlers = RoutedEventHandlerInspectionHelper.GetHandlerInfos(uiElement, routedEvent)
                    .Select(handler => new
                    {
                        handlerType = handler.HandlerType,
                        targetType = handler.TargetType,
                        methodName = handler.MethodName,
                        isClassHandler = handler.InvokeHandledEventsToo
                    })
                    .ToArray();

                return new
                {
                    success = true,
                    eventName,
                    handlerCount = handlers.Length,
                    handlers,
                    reflectionSupported = true,
                    mayBeIncomplete = true,
                    message = handlers.Length == 0
                        ? "No handlers found. Reflection does not see class handlers, commands, template triggers, or inaccessible internals."
                        : $"Found {handlers.Length} handler(s)"
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "get event handlers",
                    ex,
                    "Reflection-based handler inspection is best-effort. Verify the target event and control type, then retry.");
            }
        });
    }

}
