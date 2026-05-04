using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;


/// <summary>
/// Analyzes and traces WPF RoutedEvents
/// </summary>
public sealed partial class EventAnalyzer : DispatcherAnalyzerBase, IDisposable
{
    internal sealed record TraceSessionHandle(CancellationTokenSource TokenSource, TraceSessionMetadata Metadata);

    internal sealed record TraceStartOutcome(object Result, TraceSessionHandle? Session);

    private readonly ElementFinder _elementFinder;
    private readonly WatchEventBuffer? _watchEventBuffer;
    private readonly Func<Dispatcher?, Action, Exception?>? _cleanupInvoker;
    private readonly Action<UIElement, RoutedEvent, RoutedEventHandler, string, List<HandlerRegistration>>? _registrationInvoker;
    private readonly object _lock = new object();
    private readonly TraceEventRingBuffer _eventTrace = new(MaxEventTraceEntries);
    private readonly EventTraceTransitionState _traceTransitions = new EventTraceTransitionState();
    private readonly Dictionary<string, CompletedTraceSnapshot> _completedTraceSnapshots = new Dictionary<string, CompletedTraceSnapshot>();
    private readonly Queue<string> _completedTraceSnapshotOrder = new Queue<string>();
    private readonly Dictionary<string, TraceCleanupFailure> _traceCleanupStatuses = new Dictionary<string, TraceCleanupFailure>();
    private readonly Queue<string> _traceCleanupStatusOrder = new Queue<string>();
    private const int MaxEventTraceEntries = 10000;
    private const int MaxCompletedTraceSnapshots = 16;
    private bool _isTracing;
    private CancellationTokenSource? _tracingCts;
    private ActiveTraceSession? _activeTraceSession;
    private TraceSessionMetadata? _lastTraceMetadata;
    private TraceCleanupFailure? _lastTraceCleanupFailure;
    private int _handlerInvocationCount;
    private bool _isTraceAcceptingEvents;

    /// <summary>
    /// Create a new EventAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public EventAnalyzer(ElementFinder elementFinder)
        : this(elementFinder, null, null)
    {
    }

    internal EventAnalyzer(
        ElementFinder elementFinder,
        WatchEventBuffer? watchEventBuffer,
        Func<Dispatcher?, Action, Exception?>? cleanupInvoker = null,
        Action<UIElement, RoutedEvent, RoutedEventHandler, string, List<HandlerRegistration>>? registrationInvoker = null)
        : base(elementFinder)
    {
        _elementFinder = elementFinder;
        _watchEventBuffer = watchEventBuffer;
        _cleanupInvoker = cleanupInvoker;
        _registrationInvoker = registrationInvoker;
    }

    /// <summary>
    /// Stops any active trace session and unregisters its handlers.
    /// </summary>
    public void Dispose()
    {
        if (CleanupActiveTraceSession(out var cleanupException))
        {
            return;
        }

        if (IsStartTransitionInProgressException(cleanupException))
        {
            CancelPendingTraceStart();
            if (WaitForStartTransitionCompletion(TimeSpan.FromSeconds(1))
                && CleanupActiveTraceSession(out cleanupException))
            {
                return;
            }

            cleanupException ??= new TimeoutException("Timed out waiting for routed event trace startup cancellation to complete during dispose.");
        }

        if (IsCleanupInProgressException(cleanupException)
            && WaitForCleanupCompletion(TimeSpan.FromSeconds(1))
            && CleanupActiveTraceSession(out cleanupException))
        {
            return;
        }

        if (cleanupException != null)
        {
            LogCleanupFailure(cleanupException);
            if (ShouldAbandonActiveTraceSessionAfterDisposeFailure(cleanupException))
            {
                AbandonActiveTraceSession();
                return;
            }

            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }
    }

    /// <summary>
    /// Start tracing routed events
    /// </summary>
    public object TraceRoutedEvents(string? elementId, string eventName, int duration)
    {
        return StartTraceRoutedEvents(elementId, eventName, duration, scheduleAutoStop: true).Result;
    }

    internal TraceStartOutcome StartTraceRoutedEvents(
        string? elementId,
        string eventName,
        int duration,
        bool scheduleAutoStop = true)
    {
        if (duration < 0)
        {
            return new TraceStartOutcome(
                ToolErrorFactory.InvalidArgument("duration must be non-negative", "duration"),
                null);
        }

        var cappedDuration = Math.Min(duration, 60000); // Max 60 seconds

        return InvokeOnUIThread(() =>
        {
            var element = ResolveElement(elementId);

            if (element == null)
            {
                return new TraceStartOutcome(ToolErrorFactory.ElementNotFound(elementId), null);
            }

            if (element is not UIElement uiElement)
            {
                return new TraceStartOutcome(
                    ToolErrorFactory.InvalidArgument(
                        "Element is not a UIElement",
                        "Choose a UIElement target from get_visual_tree before tracing routed events."),
                    null);
            }

            var routedEvent = FindRoutedEvent(uiElement, eventName);
            if (routedEvent == null)
            {
                var availableEvents = RoutedEventDiscovery.EnumerateAvailableRoutedEvents(uiElement.GetType());
                return new TraceStartOutcome(ToolErrorFactory.EventNotFound(eventName, availableEvents), null);
            }

            lock (_lock)
            {
                if (_activeTraceSession == null && _isTracing)
                {
                    return new TraceStartOutcome(
                        ToolErrorFactory.OperationFailed(
                            "start routed event trace",
                            new InvalidOperationException("Another routed event trace transition is still in progress."),
                            "Wait for the current trace cleanup or startup transition to finish, then retry."),
                        null);
                }
            }

            if (!CleanupPreviousSession(out var cleanupException))
            {
                return new TraceStartOutcome(
                    ToolErrorFactory.OperationFailed(
                        "stop existing event trace",
                        cleanupException ?? new InvalidOperationException("Previous trace cleanup is still pending."),
                        "Wait for the target UI thread to become responsive, then retry starting routed event tracing."),
                    null);
            }

            var previousCleanupException = cleanupException;

            CancellationTokenSource localCts;
            lock (_lock)
            {
                _eventTrace.Clear();
                _lastTraceMetadata = null;
                _isTracing = true;
                _isTraceAcceptingEvents = false;
                _handlerInvocationCount = 0;
                _lastTraceCleanupFailure = null;
                _tracingCts = new CancellationTokenSource();
                localCts = _tracingCts;
                _activeTraceSession = null;
                _traceTransitions.BeginStart(uiElement.Dispatcher);
            }

            var registrations = new List<HandlerRegistration>();

            try
            {
                // Build handler and register on multiple points for robustness
                var traceElementId = elementId ?? _elementFinder.GenerateElementId(uiElement);
                var traceSessionId = Guid.NewGuid().ToString("N");
                var handler = CreateTraceHandler(traceElementId, traceSessionId);
                if (_registrationInvoker != null)
                {
                    _registrationInvoker(uiElement, routedEvent, handler, eventName, registrations);
                }
                else
                {
                    RegisterTraceHandlers(uiElement, routedEvent, handler, eventName, registrations);
                }

                if (localCts.IsCancellationRequested)
                {
                    return AbortPendingTraceStart(registrations, localCts);
                }

                var traceMetadata = new TraceSessionMetadata(
                    traceSessionId,
                    eventName,
                    traceElementId,
                    DateTimeOffset.UtcNow,
                    cappedDuration,
                    registrations.Count,
                    uiElement.GetType().Name);
                var sessionHandle = new TraceSessionHandle(localCts, traceMetadata);
                var canceledBeforeCommit = false;

                lock (_lock)
                {
                    canceledBeforeCommit = _traceTransitions.CancelStartTransitionRequested
                        || localCts.IsCancellationRequested;
                    if (!canceledBeforeCommit)
                    {
                        _lastTraceMetadata = traceMetadata;
                        _activeTraceSession = new ActiveTraceSession(registrations, localCts, traceMetadata);
                        _isTraceAcceptingEvents = true;
                    }

                    _traceTransitions.CompleteStart();
                }

                if (canceledBeforeCommit)
                {
                    return AbortPendingTraceStart(registrations, localCts);
                }

                if (scheduleAutoStop)
                {
                    ScheduleAutoStop(sessionHandle, cappedDuration);
                }

                return new TraceStartOutcome(
                    CreateTraceStartResult(eventName, cappedDuration, registrations.Count, previousCleanupException),
                    sessionHandle);
            }
            catch (Exception ex)
            {
                TryRollbackPartialRegistrations(registrations);
                lock (_lock)
                {
                    if (ReferenceEquals(_tracingCts, localCts))
                    {
                        _tracingCts = null;
                    }

                    _activeTraceSession = null;
                    _isTracing = false;
                    _isTraceAcceptingEvents = false;
                    _traceTransitions.CompleteStart();
                    _handlerInvocationCount = 0;
                    _eventTrace.Clear();
                }

                localCts.Dispose();

                return new TraceStartOutcome(
                    ToolErrorFactory.OperationFailed(
                        "start routed event trace",
                        ex,
                        "Verify the target UI thread is responsive and retry tracing."),
                    null);
            }
        });
    }

    /// <summary>
    /// Get event trace data
    /// </summary>
    public object GetEventTrace(string? requestedEventName = null, int? maxEvents = null)
    {
        return GetEventTrace(null, requestedEventName, maxEvents);
    }

    internal object GetEventTrace(
        TraceSessionHandle? requestedSession,
        string? requestedEventName = null,
        int? maxEvents = null)
    {
        lock (_lock)
        {
            var isActiveSession = requestedSession == null
                ? _activeTraceSession != null
                : _activeTraceSession != null
                    && ReferenceEquals(_activeTraceSession.TokenSource, requestedSession.TokenSource)
                    && ReferenceEquals(_activeTraceSession.Metadata, requestedSession.Metadata);
            var isCurrentSession = isActiveSession && _isTracing && _isTraceAcceptingEvents;
            var traceMetadata = requestedSession?.Metadata ?? _lastTraceMetadata;
            var requestedEventMatches = IsRequestedEventMatch(traceMetadata, requestedEventName);
            var completedSnapshot = ResolveCompletedSnapshot(requestedSession, requestedEventName);
            var cleanupFailure = ResolveCleanupFailure(requestedSession, traceMetadata);
            var events = GetTraceSnapshotEvents(requestedSession, requestedEventName);
            var eventPage = CreateTraceEventPage(completedSnapshot?.Events ?? events, maxEvents);
            return new
            {
                success = true,
                sessionId = traceMetadata?.SessionId,
                activeEventName = traceMetadata?.EventName,
                resolvedElementId = traceMetadata?.ElementId,
                resolvedElementType = traceMetadata?.ResolvedElementType,
                traceStartedAtUtc = traceMetadata?.StartedAtUtc,
                effectiveDurationMs = traceMetadata?.EffectiveDurationMs ?? 0,
                registrationCount = traceMetadata?.RegistrationCount ?? 0,
                isTracing = isCurrentSession,
                eventCount = eventPage.ReturnedEventCount,
                totalEventCount = eventPage.TotalEventCount,
                returnedEventCount = eventPage.ReturnedEventCount,
                eventsTruncated = eventPage.EventsTruncated,
                maxEvents = eventPage.MaxEvents,
                events = eventPage.Events,
                handlerInvocationCount = isActiveSession && requestedEventMatches
                    ? _handlerInvocationCount
                    : completedSnapshot?.HandlerInvocationCount ?? 0,
                cleanupFailed = IsCleanupStatusFailed(cleanupFailure),
                cleanupIncomplete = IsCleanupStatusIncomplete(cleanupFailure),
                cleanupState = cleanupFailure?.State,
                cleanupFailureMessage = cleanupFailure?.Message,
                cleanupFailureType = cleanupFailure?.ExceptionType
            };
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
            var element = ResolveElement(elementId);

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



































}
