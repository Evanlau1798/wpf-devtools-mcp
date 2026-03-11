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

            if (!IsReflectionSupported())
            {
                return ToolErrorFactory.OperationFailed(
                    "get event handlers",
                    new NotSupportedException("Event handler inspection is not supported on this .NET version"),
                    "This feature requires internal WPF event store reflection support that may be unavailable on the current runtime.");
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
                var handlers = GetHandlerInfoList(uiElement, routedEvent);

                return new
                {
                    success = true,
                    eventName,
                    handlerCount = handlers.Count,
                    handlers,
                    reflectionSupported = true,
                    mayBeIncomplete = true,
                    message = handlers.Count == 0
                        ? "No handlers found. Reflection does not see class handlers, commands, template triggers, or inaccessible internals."
                        : $"Found {handlers.Count} handler(s)"
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

    private static List<object> GetHandlerInfoList(UIElement uiElement, RoutedEvent routedEvent)
    {
        var handlers = new List<object>();
        var eventHandlersStore = GetEventHandlersStore(uiElement);

        if (eventHandlersStore == null)
        {
            return handlers;
        }

        var getRoutedEventHandlersMethod = eventHandlersStore.GetType().GetMethod(
            "GetRoutedEventHandlers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        if (getRoutedEventHandlersMethod == null)
        {
            return handlers;
        }

        var routedEventHandlers = getRoutedEventHandlersMethod.Invoke(
            eventHandlersStore,
            new object[] { routedEvent }) as RoutedEventHandlerInfo[];

        if (routedEventHandlers == null)
        {
            return handlers;
        }

        foreach (var handlerInfo in routedEventHandlers)
        {
            var handler = handlerInfo.Handler;
            handlers.Add(new
            {
                handlerType = handler.GetType().Name,
                targetType = handler.Target?.GetType().Name,
                methodName = handler.Method.Name,
                isClassHandler = handlerInfo.InvokeHandledEventsToo
            });
        }

        return handlers;
    }

    private static bool IsReflectionSupported()
    {
        lock (_reflectionLock)
        {
            if (_reflectionSupported.HasValue)
            {
                return _reflectionSupported.Value;
            }

            _reflectionSupported = GetEventHandlersStoreMember() != null;
            return _reflectionSupported.Value;
        }
    }

    private static object? GetEventHandlersStore(UIElement element)
    {
        var member = GetEventHandlersStoreMember();

        return member switch
        {
            System.Reflection.FieldInfo field => field.GetValue(element),
            System.Reflection.PropertyInfo property => property.GetValue(element),
            _ => null
        };
    }

    private static System.Reflection.MemberInfo? GetEventHandlersStoreMember()
    {
        var field = typeof(UIElement).GetField(
            EVENT_HANDLERS_STORE_MEMBER,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (field != null)
        {
            return field;
        }

        return typeof(UIElement).GetProperty(
            EVENT_HANDLERS_STORE_MEMBER,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    }
}
