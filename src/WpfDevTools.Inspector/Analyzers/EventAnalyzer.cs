using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;


/// <summary>
/// Analyzes and traces WPF RoutedEvents
/// </summary>
public sealed partial class EventAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;
    private readonly WatchEventBuffer? _watchEventBuffer;
    private static readonly object _lock = new object();
    private static readonly List<object> _eventTrace = new List<object>();
    private const int MaxEventTraceEntries = 10000;
    private static bool _isTracing = false;
    private static CancellationTokenSource? _tracingCts = null;
    private static ActiveTraceSession? _activeTraceSession = null;
    private static TraceSessionMetadata? _lastTraceMetadata = null;
    private static int _handlerInvocationCount = 0;

    // Reflection support for GetEventHandlers
    private const string EVENT_HANDLERS_STORE_MEMBER = "EventHandlersStore";
    private static bool? _reflectionSupported = null;
    private static readonly object _reflectionLock = new object();

    /// <summary>
    /// Create a new EventAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public EventAnalyzer(ElementFinder elementFinder)
        : this(elementFinder, null)
    {
    }

    internal EventAnalyzer(
        ElementFinder elementFinder,
        WatchEventBuffer? watchEventBuffer)
    {
        _elementFinder = elementFinder;
        _watchEventBuffer = watchEventBuffer;
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
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not UIElement uiElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a UIElement",
                    "Choose a UIElement target from get_visual_tree before tracing routed events.");
            }

            var routedEvent = FindRoutedEvent(uiElement, eventName);
            if (routedEvent == null)
            {
                var availableEvents = RoutedEventDiscovery.EnumerateAvailableRoutedEvents(uiElement.GetType());
                return ToolErrorFactory.EventNotFound(eventName, availableEvents);
            }

            CleanupPreviousSession();

            CancellationTokenSource localCts;
            lock (_lock)
            {
                _eventTrace.Clear();
                _isTracing = true;
                _handlerInvocationCount = 0;
                _tracingCts = new CancellationTokenSource();
                localCts = _tracingCts;
                _activeTraceSession = null;
            }

            // Build handler and register on multiple points for robustness
            var traceElementId = elementId ?? _elementFinder.GenerateElementId(uiElement);
            var handler = CreateTraceHandler(traceElementId);
            var registrations = RegisterTraceHandlers(uiElement, routedEvent, handler, eventName);
            var traceMetadata = new TraceSessionMetadata(
                eventName,
                traceElementId,
                DateTimeOffset.UtcNow,
                cappedDuration,
                registrations.Count,
                uiElement.GetType().Name);

            lock (_lock)
            {
                _lastTraceMetadata = traceMetadata;
                _activeTraceSession = new ActiveTraceSession(registrations, localCts, traceMetadata);
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
    public object GetEventTrace(string? requestedEventName = null)
    {
        lock (_lock)
        {
            var events = GetTraceSnapshotEvents(requestedEventName);
            return new
            {
                success = true,
                isTracing = _isTracing,
                eventCount = events.Count,
                events,
                handlerInvocationCount = _handlerInvocationCount
            };
        }
    }

    internal TraceSessionMetadata? GetLatestTraceMetadata()
    {
        lock (_lock)
        {
            return _lastTraceMetadata;
        }
    }

    internal object DrainEvents(
        int? maxEvents = null,
        string[]? eventTypes = null,
        string? elementId = null,
        DateTimeOffset? sinceTimestamp = null)
    {
        var drainedEvents = _watchEventBuffer?.Drain(
            maxEvents ?? 50,
            eventTypes,
            elementId,
            sinceTimestamp) ?? Array.Empty<WatchEventRecord>();
        var droppedEventCount = _watchEventBuffer?.ConsumeDroppedCount() ?? 0;

        if (drainedEvents.Count == 0)
        {
            return new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount
            };
        }

        return new
        {
            success = true,
            pendingEventCount = drainedEvents.Count,
            droppedEventCount,
            pendingEvents = drainedEvents.Select(CreatePendingEventContract).ToArray()
        };
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
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not UIElement uiElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a UIElement",
                    "Choose a UIElement target from get_visual_tree before firing routed events.");
            }

            var routedEvent = FindRoutedEvent(uiElement, eventName);
            if (routedEvent == null)
            {
                var availableEvents = RoutedEventDiscovery.EnumerateAvailableRoutedEvents(uiElement.GetType());
                return ToolErrorFactory.EventNotFound(eventName, availableEvents);
            }

            try
            {
                // For Click on ButtonBase, use OnClick() to include Command execution
                if (IsButtonClickEvent(uiElement, eventName))
                {
                    ButtonBaseClickHelper.InvokeOnClick((ButtonBase)uiElement);
                    EnqueueRoutedEventRecord(
                        elementId ?? _elementFinder.GenerateElementId(uiElement),
                        uiElement,
                        routedEvent,
                        handled: null,
                        originalSource: uiElement);
                    return new
                    {
                        success = true,
                        message = $"Event '{eventName}' fired via OnClick() (includes Command execution)",
                        eventName,
                        usedOnClick = true
                    };
                }

                var args = CreateRoutedEventArgs(routedEvent, uiElement);
                uiElement.RaiseEvent(args);
                EnqueueRoutedEventRecord(
                    elementId ?? _elementFinder.GenerateElementId(uiElement),
                    uiElement,
                    args.RoutedEvent,
                    args.Handled,
                    args.OriginalSource);

                return new
                {
                    success = true,
                    message = $"Event '{eventName}' fired successfully",
                    eventName
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "fire event",
                    ex,
                    "Verify the target control is loaded and that the chosen routed event is valid for its type.");
            }
        });
    }

    private RoutedEventHandler CreateTraceHandler(string tracedElementId)
    {
        return (sender, e) =>
        {
            lock (_lock)
            {
                _handlerInvocationCount++;
                if (_isTracing)
                {
                    var senderType = sender?.GetType().Name;
                    var senderName = (sender as FrameworkElement)?.Name;
                    var routingStrategy = e.RoutedEvent.RoutingStrategy.ToString();
                    var originalSourceType = (e.OriginalSource as FrameworkElement)?.GetType().Name;

                    _eventTrace.Add(new
                    {
                        timestamp = DateTime.UtcNow,
                        sender = senderType,
                        senderName,
                        eventName = e.RoutedEvent.Name,
                        routingStrategy,
                        handled = e.Handled,
                        originalSource = originalSourceType
                    });
                    EnqueueRoutedEventRecord(
                        tracedElementId,
                        sender as UIElement ?? (e.OriginalSource as UIElement) ?? throw new InvalidOperationException("Routed event trace expected a UIElement sender or original source."),
                        e.RoutedEvent,
                        e.Handled,
                        e.OriginalSource,
                        senderType,
                        senderName,
                        routingStrategy,
                        originalSourceType,
                        $"event:{tracedElementId}:{e.RoutedEvent.Name}:{_handlerInvocationCount}");

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
        if (eventName.StartsWith("Preview", StringComparison.Ordinal))
        {
            return null;
        }

        return RoutedEventDiscovery.FindRoutedEvent(element.GetType(), "Preview" + eventName);
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
        return RoutedEventDiscovery.FindRoutedEvent(element.GetType(), eventName);
    }

    private static bool IsButtonClickEvent(UIElement element, string eventName)
    {
        return element is ButtonBase
            && string.Equals(eventName, "Click", StringComparison.OrdinalIgnoreCase);
    }

    private static RoutedEventArgs CreateRoutedEventArgs(RoutedEvent routedEvent, UIElement sourceElement)
    {
        var eventArgsType = routedEvent.HandlerType?.GetMethod("Invoke")?.GetParameters().LastOrDefault()?.ParameterType;

        if (eventArgsType == typeof(MouseButtonEventArgs))
        {
            return new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = routedEvent,
                Source = sourceElement
            };
        }

        if (eventArgsType == typeof(MouseEventArgs))
        {
            return new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = routedEvent,
                Source = sourceElement
            };
        }

        return new RoutedEventArgs(routedEvent, sourceElement);
    }

    private void EnqueueRoutedEventRecord(
        string elementId,
        UIElement element,
        RoutedEvent routedEvent,
        bool? handled,
        object? originalSource,
        string? senderType = null,
        string? senderName = null,
        string? routingStrategy = null,
        string? originalSourceType = null,
        string? sourceKey = null)
    {
        _watchEventBuffer?.Enqueue(new WatchEventRecord(
            EventType: "RoutedEvent",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: sourceKey ?? $"tool:routed:{elementId}:{routedEvent.Name}:{Guid.NewGuid():N}",
            ElementId: elementId,
            PropertyName: null,
            EventName: routedEvent.Name,
            NewValue: null,
            ValueType: null,
            SenderType: senderType ?? element.GetType().Name,
            SenderName: senderName ?? (element as FrameworkElement)?.Name,
            RoutingStrategy: routingStrategy ?? routedEvent.RoutingStrategy.ToString(),
            Handled: handled,
            OriginalSourceType: originalSourceType ?? originalSource?.GetType().Name));
    }

    private static object CreatePendingEventContract(WatchEventRecord record) => new
    {
        eventType = record.EventType,
        timestampUtc = record.TimestampUtc,
        sourceKey = record.SourceKey,
        elementId = record.ElementId,
        propertyName = record.PropertyName,
        eventName = record.EventName,
        newValue = record.NewValue,
        valueType = record.ValueType,
        senderType = record.SenderType,
        senderName = record.SenderName,
        routingStrategy = record.RoutingStrategy,
        handled = record.Handled,
        originalSourceType = record.OriginalSourceType
    };
}
