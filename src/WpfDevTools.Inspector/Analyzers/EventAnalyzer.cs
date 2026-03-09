using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes and traces WPF RoutedEvents
/// </summary>
public sealed class EventAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;
    private static readonly object _lock = new object();
    private static readonly List<object> _eventTrace = new List<object>();
    private const int MaxEventTraceEntries = 10000;
    private static bool _isTracing = false;
    private static CancellationTokenSource? _tracingCts = null;
    private static ActiveTraceSession? _activeTraceSession = null;

    // Reflection support for GetEventHandlers
    private const string EVENT_HANDLERS_STORE_MEMBER = "EventHandlersStore";
    private static bool? _reflectionSupported = null;
    private static readonly object _reflectionLock = new object();

    /// <summary>
    /// Create a new EventAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public EventAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Start tracing routed events
    /// </summary>
    public object TraceRoutedEvents(string? elementId, string eventName, int duration)
    {
        var cappedDuration = Math.Min(duration, 60000); // Max 60 seconds

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

            var routedEvent = FindRoutedEvent(uiElement, eventName);
            if (routedEvent == null)
            {
                return new { success = false, error = $"Event '{eventName}' not found" };
            }

            CleanupPreviousSession();

            CancellationTokenSource localCts;
            lock (_lock)
            {
                _eventTrace.Clear();
                _isTracing = true;
                _tracingCts = new CancellationTokenSource();
                localCts = _tracingCts;
                _activeTraceSession = null;
            }

            // Build handler and register on multiple points for robustness
            var handler = CreateTraceHandler(eventName);
            var registrations = RegisterTraceHandlers(uiElement, routedEvent, handler, eventName);

            lock (_lock)
            {
                _activeTraceSession = new ActiveTraceSession(registrations, localCts);
            }

            ScheduleAutoStop(localCts, cappedDuration);

            return new
            {
                success = true,
                message = $"Started tracing '{eventName}' for {cappedDuration}ms",
                eventName,
                duration = cappedDuration,
                registrationCount = registrations.Count
            };
        });
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
        });
    }

    /// <summary>
    /// Get event handlers attached to an element
    /// </summary>
    public object GetEventHandlers(string? elementId, string eventName)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return new { success = false, error = "eventName is required" };
            }

            // Check reflection support on first use
            if (!IsReflectionSupported())
            {
                return new
                {
                    success = false,
                    error = "Event handler inspection not supported on this .NET version",
                    note = "This feature requires access to internal WPF structures that may not be available"
                };
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
                var availableEvents = RoutedEventDiscovery.EnumerateAvailableRoutedEvents(uiElement.GetType());
                return new
                {
                    success = false,
                    error = $"Event '{eventName}' not found",
                    availableEvents,
                    hint = "Use one of the availableEvents names as the eventName parameter"
                };
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
        });
    }

    private static RoutedEventHandler CreateTraceHandler(string eventName)
    {
        return (sender, e) =>
        {
            lock (_lock)
            {
                if (_isTracing)
                {
                    _eventTrace.Add(new
                    {
                        timestamp = DateTime.UtcNow,
                        sender = sender?.GetType().Name,
                        senderName = (sender as FrameworkElement)?.Name,
                        eventName = e.RoutedEvent.Name,
                        routingStrategy = e.RoutedEvent.RoutingStrategy.ToString(),
                        handled = e.Handled,
                        originalSource = (e.OriginalSource as FrameworkElement)?.GetType().Name
                    });

                    // Trim oldest entries if over limit
                    if (_eventTrace.Count > MaxEventTraceEntries)
                    {
                        _eventTrace.RemoveRange(0, _eventTrace.Count - MaxEventTraceEntries);
                    }
                }
            }
        };
    }

    private static List<HandlerRegistration> RegisterTraceHandlers(
        UIElement targetElement,
        RoutedEvent routedEvent,
        RoutedEventHandler handler,
        string eventName)
    {
        var registrations = new List<HandlerRegistration>();

        // 1. Register on the target element
        targetElement.AddHandler(routedEvent, handler, handledEventsToo: true);
        registrations.Add(new HandlerRegistration(targetElement, routedEvent, handler));

        // 2. Register on root window for bubble/tunnel capture
        try
        {
            var rootWindow = Window.GetWindow(targetElement);
            if (rootWindow != null && !ReferenceEquals(rootWindow, targetElement))
            {
                rootWindow.AddHandler(routedEvent, handler, handledEventsToo: true);
                registrations.Add(new HandlerRegistration(rootWindow, routedEvent, handler));
            }
        }
        catch (InvalidOperationException)
        {
            // Window.GetWindow may fail with cross-thread access; safe to skip
        }

        // 3. Try to find and register the Preview (tunneling) variant
        var previewEvent = FindPreviewRoutedEvent(targetElement, eventName);
        if (previewEvent != null)
        {
            targetElement.AddHandler(previewEvent, handler, handledEventsToo: true);
            registrations.Add(new HandlerRegistration(targetElement, previewEvent, handler));
        }

        return registrations;
    }

    private static RoutedEvent? FindPreviewRoutedEvent(UIElement element, string eventName)
    {
        // If it already starts with "Preview", skip
        if (eventName.StartsWith("Preview", StringComparison.Ordinal))
        {
            return null;
        }

        var previewFieldName = "Preview" + eventName + "Event";
        var type = element.GetType();

        while (type != null && type != typeof(object))
        {
            var field = type.GetField(previewFieldName,
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

    private void CleanupPreviousSession()
    {
        ActiveTraceSession? previousSession;
        lock (_lock)
        {
            previousSession = _activeTraceSession;
            if (previousSession != null)
            {
                previousSession.TokenSource.Cancel();
                previousSession.TokenSource.Dispose();
                _activeTraceSession = null;
            }
        }

        if (previousSession != null)
        {
            RemoveAllHandlers(previousSession.Registrations);
        }
    }

    private static void RemoveAllHandlers(List<HandlerRegistration> registrations)
    {
        foreach (var reg in registrations)
        {
            try
            {
                reg.Element.RemoveHandler(reg.RoutedEvent, reg.Handler);
            }
            catch
            {
                // Element may have been disposed; safe to ignore
            }
        }
    }

    private void ScheduleAutoStop(CancellationTokenSource localCts, int cappedDuration)
    {
        Task.Delay(cappedDuration, localCts.Token).ContinueWith(_ =>
        {
            InvokeOnUIThread(() =>
            {
                lock (_lock)
                {
                    if (_activeTraceSession != null &&
                        ReferenceEquals(_activeTraceSession.TokenSource, localCts))
                    {
                        RemoveAllHandlers(_activeTraceSession.Registrations);
                        _activeTraceSession = null;
                        _isTracing = false;
                    }
                }
            });
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
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

    /// <summary>
    /// Check if reflection-based event handler inspection is supported
    /// </summary>
    private static bool IsReflectionSupported()
    {
        lock (_reflectionLock)
        {
            // Cache the result after first check
            if (_reflectionSupported.HasValue)
            {
                return _reflectionSupported.Value;
            }

            // Check if the internal field exists
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
