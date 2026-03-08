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

    private sealed class ActiveTraceSession
    {
        public ActiveTraceSession(
            UIElement element,
            RoutedEvent routedEvent,
            RoutedEventHandler handler,
            CancellationTokenSource tokenSource)
        {
            Element = element;
            RoutedEvent = routedEvent;
            Handler = handler;
            TokenSource = tokenSource;
        }

        public UIElement Element { get; }

        public RoutedEvent RoutedEvent { get; }

        public RoutedEventHandler Handler { get; }

        public CancellationTokenSource TokenSource { get; }
    }

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

            ActiveTraceSession? previousSession;
            CancellationTokenSource localCts;
            lock (_lock)
            {
                previousSession = _activeTraceSession;
                _eventTrace.Clear();
                _isTracing = true;

                previousSession?.TokenSource.Cancel();
                previousSession?.TokenSource.Dispose();
                _tracingCts = new CancellationTokenSource();
                localCts = _tracingCts;
                _activeTraceSession = null;
            }

            if (previousSession != null)
            {
                previousSession.Element.RemoveHandler(previousSession.RoutedEvent, previousSession.Handler);
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

                        // Trim oldest entries if over limit
                        if (_eventTrace.Count > MaxEventTraceEntries)
                        {
                            _eventTrace.RemoveRange(0, _eventTrace.Count - MaxEventTraceEntries);
                        }
                    }
                }
            });

            uiElement.AddHandler(routedEvent, handler, handledEventsToo: true);

            lock (_lock)
            {
                _activeTraceSession = new ActiveTraceSession(uiElement, routedEvent, handler, localCts);
            }

            Task.Delay(cappedDuration, localCts.Token).ContinueWith(_ =>
            {
                InvokeOnUIThread(() =>
                {
                    lock (_lock)
                    {
                        if (_activeTraceSession != null && ReferenceEquals(_activeTraceSession.TokenSource, localCts))
                        {
                            _activeTraceSession.Element.RemoveHandler(_activeTraceSession.RoutedEvent, _activeTraceSession.Handler);
                            _activeTraceSession = null;
                            _isTracing = false;
                        }
                    }
                });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            return new
            {
                success = true,
                message = $"Started tracing '{eventName}' for {cappedDuration}ms",
                eventName,
                duration = cappedDuration
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
                return new { success = false, error = $"Event '{eventName}' not found" };
            }

            try
            {
                var handlers = new List<object>();

                // Use reflection to get event handlers
                // Note: This accesses internal WPF structures and may not work in all .NET versions
                var eventHandlersStore = GetEventHandlersStore(uiElement);

                if (eventHandlersStore == null)
                {
                    return new
                    {
                        success = true,
                        eventName,
                        handlerCount = 0,
                        handlers = Array.Empty<object>(),
                        message = "No handlers found"
                    };
                }

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
