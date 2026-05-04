using System.Windows;
using System.Windows.Threading;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class EventAnalyzer
{
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
        if (traceMetadata == null)
        {
            return null;
        }

        if (_traceCleanupStatuses.TryGetValue(traceMetadata.SessionId, out var cleanupStatus))
        {
            return cleanupStatus;
        }

        if (_lastTraceCleanupFailure == null)
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
            _traceTransitions.CompleteCleanup();
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
            _traceTransitions.CompleteStart();
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
            _traceTransitions.CompleteStart();
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
            if (_traceTransitions.IsStartTransitionInProgress)
            {
                _traceTransitions.RequestStartCancellation();
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
            _traceTransitions.CompleteStart();
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
            transitionDispatcher = _traceTransitions.StartTransitionDispatcher;
        }

        if (transitionDispatcher != null && transitionDispatcher.CheckAccess())
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (!_traceTransitions.IsStartTransitionInProgress)
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
                if (!_traceTransitions.IsStartTransitionInProgress)
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
            cleanupDispatcher = _traceTransitions.CleanupTransitionDispatcher;
        }

        if (cleanupDispatcher != null && cleanupDispatcher.CheckAccess())
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (!_traceTransitions.IsCleanupInProgress)
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
                return !_traceTransitions.IsCleanupInProgress;
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
                return !_traceTransitions.IsCleanupInProgress;
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
}
