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
    private readonly Dictionary<string, CompletedTraceSnapshot> _completedTraceSnapshots = new Dictionary<string, CompletedTraceSnapshot>();
    private readonly Queue<string> _completedTraceSnapshotOrder = new Queue<string>();
    private const int MaxEventTraceEntries = 10000;
    private const int MaxCompletedTraceSnapshots = 16;
    private bool _isTracing;
    private CancellationTokenSource? _tracingCts;
    private ActiveTraceSession? _activeTraceSession;
    private TraceSessionMetadata? _lastTraceMetadata;
    private TraceCleanupFailure? _lastTraceCleanupFailure;
    private int _handlerInvocationCount;
    private bool _isTraceAcceptingEvents;
    private bool _isCleanupInProgress;
    private Dispatcher? _cleanupTransitionDispatcher;
    private bool _isStartTransitionInProgress;
    private Dispatcher? _startTransitionDispatcher;
    private bool _cancelStartTransitionRequested;

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
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

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

            CancellationTokenSource localCts;
            lock (_lock)
            {
                _eventTrace.Clear();
                _lastTraceMetadata = null;
                _isTracing = true;
                _isTraceAcceptingEvents = false;
                _isStartTransitionInProgress = true;
                _cancelStartTransitionRequested = false;
                _handlerInvocationCount = 0;
                _lastTraceCleanupFailure = null;
                _tracingCts = new CancellationTokenSource();
                localCts = _tracingCts;
                _activeTraceSession = null;
                _startTransitionDispatcher = uiElement.Dispatcher;
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
                    canceledBeforeCommit = _cancelStartTransitionRequested || localCts.IsCancellationRequested;
                    if (!canceledBeforeCommit)
                    {
                        _lastTraceMetadata = traceMetadata;
                        _activeTraceSession = new ActiveTraceSession(registrations, localCts, traceMetadata);
                        _isTraceAcceptingEvents = true;
                    }

                    _isStartTransitionInProgress = false;
                    _startTransitionDispatcher = null;
                    _cancelStartTransitionRequested = false;
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
                    new
                    {
                        success = true,
                        message = $"Started tracing '{eventName}' for {cappedDuration}ms",
                        eventName,
                        duration = cappedDuration,
                        registrationCount = registrations.Count
                    },
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
                    _isStartTransitionInProgress = false;
                    _startTransitionDispatcher = null;
                    _cancelStartTransitionRequested = false;
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
    public object GetEventTrace(string? requestedEventName = null)
    {
        return GetEventTrace(null, requestedEventName);
    }

    internal object GetEventTrace(TraceSessionHandle? requestedSession, string? requestedEventName = null)
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
                eventCount = completedSnapshot?.Events.Count ?? events.Count,
                events = completedSnapshot?.Events ?? events,
                handlerInvocationCount = isActiveSession && requestedEventMatches
                    ? _handlerInvocationCount
                    : completedSnapshot?.HandlerInvocationCount ?? 0,
                cleanupFailed = cleanupFailure != null,
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

    private RoutedEventHandler CreateTraceHandler(string tracedElementId, string traceSessionId)
    {
        return (sender, e) =>
        {
            lock (_lock)
            {
                if (_isTraceAcceptingEvents
                    && string.Equals(_activeTraceSession?.Metadata.SessionId, traceSessionId, StringComparison.Ordinal))
                {
                    _handlerInvocationCount++;
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
                        $"event:{traceSessionId}:{tracedElementId}:{e.RoutedEvent.Name}:{_handlerInvocationCount}");
                }
            }
        };
    }

    private static void RegisterTraceHandlers(
        UIElement targetElement,
        RoutedEvent routedEvent,
        RoutedEventHandler handler,
        string eventName,
        List<HandlerRegistration> registrations)
    {
        try
        {
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
        }
        catch
        {
            TryRollbackPartialRegistrations(registrations);
            throw;
        }
    }

    private static RoutedEvent? FindPreviewRoutedEvent(UIElement element, string eventName)
    {
        if (eventName.StartsWith("Preview", StringComparison.Ordinal))
        {
            return null;
        }

        return RoutedEventDiscovery.FindRoutedEvent(element.GetType(), "Preview" + eventName);
    }

    private bool CleanupPreviousSession(out Exception? cleanupException)
    {
        return CleanupTraceSessionCore(null, out cleanupException, treatDeferredCleanupAsSuccess: true);
    }

    internal bool CleanupActiveTraceSession(out Exception? cleanupException)
    {
        return CleanupTraceSessionCore(null, out cleanupException, treatDeferredCleanupAsSuccess: true);
    }

    internal bool CleanupTraceSession(TraceSessionHandle expectedSession, out Exception? cleanupException)
    {
        return CleanupTraceSessionCore(expectedSession, out cleanupException, treatDeferredCleanupAsSuccess: false);
    }

    private bool CleanupTraceSessionCore(
        TraceSessionHandle? expectedSession,
        out Exception? cleanupException,
        bool treatDeferredCleanupAsSuccess)
    {
        ActiveTraceSession? sessionToClean = null;
        CompletedTraceSnapshot? completedSnapshot = null;
        cleanupException = null;

        lock (_lock)
        {
            if (_activeTraceSession == null)
            {
                if (_isStartTransitionInProgress)
                {
                    cleanupException = new InvalidOperationException("Routed event trace startup is still in progress.");
                    return false;
                }

                return true;
            }

            if (_isCleanupInProgress)
            {
                cleanupException = new InvalidOperationException("Routed event trace cleanup is already in progress.");
                return false;
            }

            if (expectedSession != null
                && (!ReferenceEquals(_activeTraceSession.TokenSource, expectedSession.TokenSource)
                    || !ReferenceEquals(_activeTraceSession.Metadata, expectedSession.Metadata)))
            {
                return true;
            }

            sessionToClean = _activeTraceSession;
            _isCleanupInProgress = true;
            _isTraceAcceptingEvents = false;
            _cleanupTransitionDispatcher = sessionToClean.Registrations.Count > 0
                ? sessionToClean.Registrations[0].Element.Dispatcher
                : Application.Current?.Dispatcher;
            completedSnapshot = new CompletedTraceSnapshot(_eventTrace.GetSnapshot(), _handlerInvocationCount);
        }

        var removalSucceeded = TryCancelAndRemoveTraceSession(
            sessionToClean,
            out cleanupException,
            out var deferredCleanupScheduled);

        lock (_lock)
        {
            if (removalSucceeded || deferredCleanupScheduled)
            {
                StoreCompletedSnapshot(sessionToClean.Metadata.SessionId, completedSnapshot);

                if (removalSucceeded)
                {
                    if (_lastTraceCleanupFailure != null
                        && string.Equals(_lastTraceCleanupFailure.SessionId, sessionToClean.Metadata.SessionId, StringComparison.Ordinal))
                    {
                        _lastTraceCleanupFailure = null;
                    }
                }
                else
                {
                    _lastTraceCleanupFailure = new TraceCleanupFailure(
                        sessionToClean.Metadata.SessionId,
                        sessionToClean.Metadata.EventName,
                        cleanupException?.GetType().Name ?? nameof(InvalidOperationException),
                        cleanupException?.Message ?? "Routed event trace cleanup failed.");
                }

                DeactivateTraceSession(sessionToClean);

                _isCleanupInProgress = false;
                _cleanupTransitionDispatcher = null;

                return removalSucceeded || treatDeferredCleanupAsSuccess;
            }

            _lastTraceCleanupFailure = new TraceCleanupFailure(
                sessionToClean.Metadata.SessionId,
                sessionToClean.Metadata.EventName,
                cleanupException?.GetType().Name ?? nameof(InvalidOperationException),
                cleanupException?.Message ?? "Routed event trace cleanup failed.");

            _isCleanupInProgress = false;
            _cleanupTransitionDispatcher = null;
            _isTracing = true;
            _isTraceAcceptingEvents = false;

            return false;
        }
    }

    private bool TryCancelAndRemoveTraceSession(
        ActiveTraceSession session,
        out Exception? cleanupException,
        out bool deferredCleanupScheduled)
    {
        cleanupException = null;
        deferredCleanupScheduled = false;

        try
        {
            session.TokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var dispatcher = session.Registrations.Count > 0
            ? session.Registrations[0].Element.Dispatcher
            : Application.Current?.Dispatcher;

        try
        {
            if (_cleanupInvoker != null)
            {
                cleanupException = _cleanupInvoker(dispatcher, () => RemoveAllHandlers(session.Registrations));
                if (cleanupException != null)
                {
                    LogCleanupFailure(cleanupException);
                    if (ShouldTreatAsTerminalCleanup(dispatcher, cleanupException))
                    {
                        DisposeTokenSource(session.TokenSource);
                        cleanupException = null;
                        return true;
                    }

                    deferredCleanupScheduled = TryScheduleDeferredHandlerRemoval(session, dispatcher);
                    if (deferredCleanupScheduled)
                    {
                        DisposeTokenSource(session.TokenSource);
                    }

                    return false;
                }
            }
            else
            {
                InvokeOnDispatcher(dispatcher, () => RemoveAllHandlers(session.Registrations));
            }

            DisposeTokenSource(session.TokenSource);
            return true;
        }
        catch (TimeoutException ex)
        {
            cleanupException = ex;
            LogCleanupFailure(ex);
            deferredCleanupScheduled = TryScheduleDeferredHandlerRemoval(session, dispatcher);
            if (deferredCleanupScheduled)
            {
                DisposeTokenSource(session.TokenSource);
            }
            return false;
        }
        catch (InvalidOperationException ex)
        {
            cleanupException = ex;
            LogCleanupFailure(ex);
            if (ShouldTreatAsTerminalCleanup(dispatcher, ex))
            {
                DisposeTokenSource(session.TokenSource);
                cleanupException = null;
                return true;
            }

            deferredCleanupScheduled = TryScheduleDeferredHandlerRemoval(session, dispatcher);
            if (deferredCleanupScheduled)
            {
                DisposeTokenSource(session.TokenSource);
            }

            return false;
        }
        catch (Exception ex)
        {
            cleanupException = ex;
            LogCleanupFailure(ex);
            if (ShouldTreatAsTerminalCleanup(dispatcher, ex))
            {
                DisposeTokenSource(session.TokenSource);
                cleanupException = null;
                return true;
            }

            deferredCleanupScheduled = TryScheduleDeferredHandlerRemoval(session, dispatcher);
            if (deferredCleanupScheduled)
            {
                DisposeTokenSource(session.TokenSource);
            }

            return false;
        }
    }

    private static void DisposeTokenSource(CancellationTokenSource tokenSource)
    {
        try
        {
            tokenSource.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static bool TryScheduleDeferredHandlerRemoval(ActiveTraceSession session, Dispatcher? dispatcher)
    {
        if (session.Registrations.Count == 0
            || dispatcher == null
            || dispatcher.HasShutdownStarted
            || dispatcher.HasShutdownFinished)
        {
            return false;
        }

        try
        {
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    try
                    {
                        RemoveAllHandlers(session.Registrations);
                    }
                    catch (Exception ex)
                    {
                        LogCleanupFailure(ex);
                    }
                }));
            return true;
        }
        catch (InvalidOperationException ex) when (ShouldTreatAsTerminalCleanup(dispatcher, ex))
        {
            LogCleanupFailure(ex);
            return false;
        }
    }

    private static bool ShouldTreatAsTerminalCleanup(Dispatcher? dispatcher, Exception exception)
    {
        if (dispatcher?.HasShutdownStarted == true || dispatcher?.HasShutdownFinished == true)
        {
            return true;
        }

        if (exception is AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Count > 0
                && aggregateException.InnerExceptions.All(inner => ShouldTreatAsTerminalCleanup(dispatcher, inner));
        }

        if (exception is InvalidOperationException invalidOperationException)
        {
            return IsDispatcherShutdownMessage(invalidOperationException.Message)
                || (invalidOperationException.InnerException != null
                    && ShouldTreatAsTerminalCleanup(dispatcher, invalidOperationException.InnerException));
        }

        if (exception is ObjectDisposedException)
        {
            return true;
        }

        if (exception.InnerException != null)
        {
            return ShouldTreatAsTerminalCleanup(dispatcher, exception.InnerException);
        }

        return false;
    }

    private static bool IsDispatcherShutdownMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var dispatcherMessage = message!;

        return dispatcherMessage.IndexOf("dispatcher", StringComparison.OrdinalIgnoreCase) >= 0
            && (dispatcherMessage.IndexOf("shut down", StringComparison.OrdinalIgnoreCase) >= 0
                || dispatcherMessage.IndexOf("shutdown", StringComparison.OrdinalIgnoreCase) >= 0
                || dispatcherMessage.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void LogCleanupFailure(Exception exception)
    {
        switch (exception)
        {
            case TimeoutException timeoutException:
                System.Diagnostics.Trace.TraceWarning($"Timed out while removing routed event trace handlers: {timeoutException.Message}");
                break;
            case InvalidOperationException invalidOperationException:
                System.Diagnostics.Trace.TraceWarning($"Skipped routed event trace handler removal because the dispatcher was unavailable during teardown: {invalidOperationException.Message}");
                break;
            default:
                System.Diagnostics.Trace.TraceError($"Unexpected failure while removing routed event trace handlers during teardown: {exception}");
                break;
        }
    }

    private static void RemoveAllHandlers(List<HandlerRegistration> registrations)
    {
        List<Exception>? failures = null;

        foreach (var reg in registrations)
        {
            try
            {
                reg.Element.RemoveHandler(reg.RoutedEvent, reg.Handler);
            }
            catch (Exception ex)
            {
                failures ??= new List<Exception>();
                failures.Add(new InvalidOperationException(
                    $"Failed to remove routed event handler '{reg.RoutedEvent.Name}' from '{reg.Element.GetType().Name}'.",
                    ex));
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException("One or more routed event trace handlers could not be removed.", failures);
        }
    }

    private CompletedTraceSnapshot? ResolveCompletedSnapshot(TraceSessionHandle? requestedSession, string? requestedEventName)
    {
        var traceMetadata = requestedSession?.Metadata ?? _lastTraceMetadata;
        if (!IsRequestedEventMatch(traceMetadata, requestedEventName))
        {
            return null;
        }

        if (requestedSession != null)
        {
            return _completedTraceSnapshots.TryGetValue(requestedSession.Metadata.SessionId, out var requestedSnapshot)
                ? requestedSnapshot
                : null;
        }

        return _lastTraceMetadata != null
            && _completedTraceSnapshots.TryGetValue(_lastTraceMetadata.SessionId, out var latestSnapshot)
                ? latestSnapshot
                : null;
    }

    private TraceCleanupFailure? ResolveCleanupFailure(
        TraceSessionHandle? requestedSession,
        TraceSessionMetadata? traceMetadata)
    {
        if (_lastTraceCleanupFailure == null || traceMetadata == null)
        {
            return null;
        }

        return string.Equals(_lastTraceCleanupFailure.SessionId, traceMetadata.SessionId, StringComparison.Ordinal)
            ? _lastTraceCleanupFailure
            : null;
    }

    private static bool IsRequestedEventMatch(TraceSessionMetadata? traceMetadata, string? requestedEventName)
    {
        return string.IsNullOrWhiteSpace(requestedEventName)
            || traceMetadata == null
            || string.Equals(requestedEventName, traceMetadata.EventName, StringComparison.OrdinalIgnoreCase);
    }

    private void AbandonActiveTraceSession()
    {
        CancellationTokenSource? tokenSourceToDispose = null;

        lock (_lock)
        {
            if (_activeTraceSession != null)
            {
                tokenSourceToDispose = _activeTraceSession.TokenSource;
            }

            _activeTraceSession = null;
            _tracingCts = null;
            _lastTraceCleanupFailure = null;
            _isTracing = false;
            _isTraceAcceptingEvents = false;
            _isCleanupInProgress = false;
            _cleanupTransitionDispatcher = null;
        }

        if (tokenSourceToDispose != null)
        {
            try
            {
                tokenSourceToDispose.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            tokenSourceToDispose.Dispose();
        }
    }

    private void DeactivateTraceSession(ActiveTraceSession session)
    {
        if (ReferenceEquals(_activeTraceSession, session))
        {
            _activeTraceSession = null;
        }

        if (ReferenceEquals(_tracingCts, session.TokenSource))
        {
            _tracingCts = null;
        }

        if (_activeTraceSession == null)
        {
            _isTracing = false;
            _isTraceAcceptingEvents = false;
            _isStartTransitionInProgress = false;
            _startTransitionDispatcher = null;
        }
    }

    private void AbandonPendingStartTransition()
    {
        CancellationTokenSource? tokenSourceToDispose = null;

        lock (_lock)
        {
            tokenSourceToDispose = _tracingCts;
            _tracingCts = null;
            _activeTraceSession = null;
            _lastTraceCleanupFailure = null;
            _isTracing = false;
            _isTraceAcceptingEvents = false;
            _isStartTransitionInProgress = false;
            _startTransitionDispatcher = null;
            _cancelStartTransitionRequested = false;
        }

        if (tokenSourceToDispose != null)
        {
            try
            {
                tokenSourceToDispose.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            tokenSourceToDispose.Dispose();
        }
    }

    private static bool IsCleanupInProgressException(Exception? exception)
    {
        return exception is InvalidOperationException invalidOperationException
            && string.Equals(
                invalidOperationException.Message,
                "Routed event trace cleanup is already in progress.",
                StringComparison.Ordinal);
    }

    private static bool IsStartTransitionInProgressException(Exception? exception)
    {
        return exception is InvalidOperationException invalidOperationException
            && string.Equals(
                invalidOperationException.Message,
                "Routed event trace startup is still in progress.",
                StringComparison.Ordinal);
    }

    private bool ShouldAbandonActiveTraceSessionAfterDisposeFailure(Exception cleanupException)
    {
        ActiveTraceSession? activeTraceSession;
        lock (_lock)
        {
            activeTraceSession = _activeTraceSession;
        }

        if (activeTraceSession == null)
        {
            return true;
        }

        var dispatcher = activeTraceSession.Registrations.Count > 0
            ? activeTraceSession.Registrations[0].Element.Dispatcher
            : Application.Current?.Dispatcher;
        return ShouldTreatAsTerminalCleanup(dispatcher, cleanupException);
    }

    private void CancelPendingTraceStart()
    {
        CancellationTokenSource? tokenSource;

        lock (_lock)
        {
            if (_isStartTransitionInProgress)
            {
                _cancelStartTransitionRequested = true;
                tokenSource = _tracingCts;
            }
            else
            {
                tokenSource = null;
            }
        }

        if (tokenSource == null)
        {
            return;
        }

        try
        {
            tokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private TraceStartOutcome AbortPendingTraceStart(
        List<HandlerRegistration> registrations,
        CancellationTokenSource localCts)
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
            _isStartTransitionInProgress = false;
            _startTransitionDispatcher = null;
            _cancelStartTransitionRequested = false;
        }

        localCts.Dispose();

        return new TraceStartOutcome(
            ToolErrorFactory.OperationFailed(
                "start routed event trace",
                new OperationCanceledException("Routed event trace startup was canceled before activation."),
                "Retry tracing after the current shutdown or cleanup finishes."),
            null);
    }

    private bool WaitForStartTransitionCompletion(TimeSpan timeout)
    {
        Dispatcher? transitionDispatcher;
        lock (_lock)
        {
            transitionDispatcher = _startTransitionDispatcher;
        }

        if (transitionDispatcher != null && transitionDispatcher.CheckAccess())
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (!_isStartTransitionInProgress)
                    {
                        return true;
                    }
                }

                if (transitionDispatcher.HasShutdownStarted || transitionDispatcher.HasShutdownFinished)
                {
                    AbandonPendingStartTransition();
                    return true;
                }

                try
                {
                    var frame = new DispatcherFrame();
                    _ = transitionDispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }
                catch (InvalidOperationException ex) when (ShouldTreatAsTerminalCleanup(transitionDispatcher, ex))
                {
                    AbandonPendingStartTransition();
                    return true;
                }
            }

            return false;
        }

        var waitUntil = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < waitUntil)
        {
            lock (_lock)
            {
                if (!_isStartTransitionInProgress)
                {
                    return true;
                }
            }

            if (transitionDispatcher?.HasShutdownStarted == true || transitionDispatcher?.HasShutdownFinished == true)
            {
                AbandonPendingStartTransition();
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private bool WaitForCleanupCompletion(TimeSpan timeout)
    {
        Dispatcher? cleanupDispatcher;
        lock (_lock)
        {
            cleanupDispatcher = _cleanupTransitionDispatcher;
        }

        if (cleanupDispatcher != null && cleanupDispatcher.CheckAccess())
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (!_isCleanupInProgress)
                    {
                        return true;
                    }
                }

                if (cleanupDispatcher.HasShutdownStarted || cleanupDispatcher.HasShutdownFinished)
                {
                    AbandonActiveTraceSession();
                    return true;
                }

                try
                {
                    var frame = new DispatcherFrame();
                    cleanupDispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }
                catch (InvalidOperationException ex) when (ShouldTreatAsTerminalCleanup(cleanupDispatcher, ex))
                {
                    AbandonActiveTraceSession();
                    return true;
                }
            }

            lock (_lock)
            {
                return !_isCleanupInProgress;
            }
        }

        return SpinWait.SpinUntil(() =>
        {
            if (cleanupDispatcher?.HasShutdownStarted == true || cleanupDispatcher?.HasShutdownFinished == true)
            {
                AbandonActiveTraceSession();
                return true;
            }

            lock (_lock)
            {
                return !_isCleanupInProgress;
            }
        }, timeout);
    }

    private void StoreCompletedSnapshot(string sessionId, CompletedTraceSnapshot completedSnapshot)
    {
        _completedTraceSnapshots[sessionId] = completedSnapshot;
        _completedTraceSnapshotOrder.Enqueue(sessionId);

        while (_completedTraceSnapshots.Count > MaxCompletedTraceSnapshots && _completedTraceSnapshotOrder.Count > 0)
        {
            var oldestSessionId = _completedTraceSnapshotOrder.Dequeue();
            _completedTraceSnapshots.Remove(oldestSessionId);
        }
    }

    private static void TryRollbackPartialRegistrations(List<HandlerRegistration> registrations)
    {
        if (registrations.Count == 0)
        {
            return;
        }

        try
        {
            RemoveAllHandlers(registrations);
        }
        catch
        {
        }
    }

    private void ScheduleAutoStop(TraceSessionHandle sessionHandle, int cappedDuration)
    {
        Task.Delay(cappedDuration, sessionHandle.TokenSource.Token).ContinueWith(completedDelay =>
        {
            CleanupTraceSessionCore(sessionHandle, out _, treatDeferredCleanupAsSuccess: false);
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
