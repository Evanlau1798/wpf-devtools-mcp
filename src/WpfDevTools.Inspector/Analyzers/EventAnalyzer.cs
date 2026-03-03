using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes and traces WPF RoutedEvents
/// </summary>
public class EventAnalyzer
{
    private readonly ElementFinder _elementFinder;
    private static readonly object _lock = new object();
    private static readonly List<object> _eventTrace = new List<object>();
    private static bool _isTracing = false;

    public EventAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Start tracing routed events
    /// </summary>
    public object TraceRoutedEvents(string? elementId, string eventName, int duration)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                TraceRoutedEvents(elementId, eventName, duration));
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

        var routedEvent = FindRoutedEvent(uiElement, eventName);
        if (routedEvent == null)
        {
            return new { success = false, error = $"Event '{eventName}' not found" };
        }

        lock (_lock)
        {
            _eventTrace.Clear();
            _isTracing = true;
        }

        // Register event handler
        var handler = new RoutedEventHandler((sender, e) =>
        {
            lock (_lock)
            {
                if (_isTracing)
                {
                    _eventTrace.Add(new
                    {
                        timestamp = DateTime.UtcNow,
                        sender = sender?.GetType().Name,
                        routingStrategy = e.RoutedEvent.RoutingStrategy.ToString(),
                        handled = e.Handled
                    });
                }
            }
        });

        uiElement.AddHandler(routedEvent, handler);

        // Stop tracing after duration
        Task.Delay(duration).ContinueWith(_ =>
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    uiElement.RemoveHandler(routedEvent, handler);
                    lock (_lock)
                    {
                        _isTracing = false;
                    }
                });
            }
        });

        return new
        {
            success = true,
            message = $"Started tracing '{eventName}' for {duration}ms",
            eventName,
            duration
        };
    }

    /// <summary>
    /// Get event trace data
    /// </summary>
    public object GetEventTrace()
    {
        lock (_lock)
        {
            return new
            {
                success = true,
                isTracing = _isTracing,
                eventCount = _eventTrace.Count,
                events = _eventTrace.ToList()
            };
        }
    }

    /// <summary>
    /// Fire a routed event
    /// </summary>
    public object FireRoutedEvent(string? elementId, string eventName, object? eventArgs)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                FireRoutedEvent(elementId, eventName, eventArgs));
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

        var routedEvent = FindRoutedEvent(uiElement, eventName);
        if (routedEvent == null)
        {
            return new { success = false, error = $"Event '{eventName}' not found" };
        }

        try
        {
            var args = new RoutedEventArgs(routedEvent, uiElement);
            uiElement.RaiseEvent(args);

            return new
            {
                success = true,
                message = $"Event '{eventName}' fired successfully",
                eventName
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to fire event: {ex.Message}" };
        }
    }

    private RoutedEvent? FindRoutedEvent(UIElement element, string eventName)
    {
        var type = element.GetType();
        var fieldName = eventName + "Event";

        // Search in current type and base types
        while (type != null && type != typeof(object))
        {
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);

            if (field != null && field.FieldType == typeof(RoutedEvent))
            {
                return field.GetValue(null) as RoutedEvent;
            }

            type = type.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Get event handlers attached to an element
    /// </summary>
    public object GetEventHandlers(string? elementId, string eventName)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                GetEventHandlers(elementId, eventName));
        }

        if (string.IsNullOrEmpty(eventName))
        {
            return new { success = false, error = "eventName is required" };
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not UIElement uiElement)
        {
            return new { error = "Element is not a UIElement" };
        }

        var routedEvent = FindRoutedEvent(uiElement, eventName);
        if (routedEvent == null)
        {
            return new { success = false, error = $"Event '{eventName}' not found" };
        }

        try
        {
            var handlers = new List<object>();

            // Use reflection to get event handlers
            // Note: This is a simplified implementation as WPF doesn't provide direct access to handlers
            var eventHandlersStoreField = typeof(UIElement).GetField(
                "EventHandlersStore",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (eventHandlersStoreField != null)
            {
                var eventHandlersStore = eventHandlersStoreField.GetValue(uiElement);
                if (eventHandlersStore != null)
                {
                    var getRoutedEventHandlersMethod = eventHandlersStore.GetType().GetMethod(
                        "GetRoutedEventHandlers",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

                    if (getRoutedEventHandlersMethod != null)
                    {
                        var routedEventHandlers = getRoutedEventHandlersMethod.Invoke(
                            eventHandlersStore,
                            new object[] { routedEvent }) as RoutedEventHandlerInfo[];

                        if (routedEventHandlers != null)
                        {
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
                        }
                    }
                }
            }

            return new
            {
                success = true,
                eventName,
                handlerCount = handlers.Count,
                handlers,
                message = handlers.Count == 0
                    ? "No handlers found (or handlers are not accessible via reflection)"
                    : $"Found {handlers.Count} handler(s)"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                error = $"Failed to get event handlers: {ex.Message}",
                note = "Event handler inspection is limited due to WPF internal structure"
            };
        }
    }
}
