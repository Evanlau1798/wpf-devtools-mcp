using System.Windows;
using System.Windows.Threading;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class EventAnalyzer
{
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
                if (_traceTransitions.IsStartTransitionInProgress)
                {
                    cleanupException = new InvalidOperationException("Routed event trace startup is still in progress.");
                    return false;
                }

                return true;
            }

            if (_traceTransitions.IsCleanupInProgress)
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
            _isTraceAcceptingEvents = false;
            var cleanupDispatcher = sessionToClean.Registrations.Count > 0
                ? sessionToClean.Registrations[0].Element.Dispatcher
                : Application.Current?.Dispatcher;
            if (!_traceTransitions.TryBeginCleanup(cleanupDispatcher))
            {
                cleanupException = new InvalidOperationException("Routed event trace cleanup is already in progress.");
                return false;
            }

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

                    RemoveTraceCleanupStatus(sessionToClean.Metadata.SessionId);
                }
                else
                {
                    StoreTraceCleanupStatus(new TraceCleanupFailure(
                        sessionToClean.Metadata.SessionId,
                        sessionToClean.Metadata.EventName,
                        cleanupException?.GetType().Name ?? nameof(InvalidOperationException),
                        cleanupException?.Message ?? "Routed event trace cleanup failed.",
                        CleanupStateDeferredPending));
                }

                DeactivateTraceSession(sessionToClean);

                _traceTransitions.CompleteCleanup();

                return removalSucceeded || treatDeferredCleanupAsSuccess;
            }

            StoreTraceCleanupStatus(new TraceCleanupFailure(
                sessionToClean.Metadata.SessionId,
                sessionToClean.Metadata.EventName,
                cleanupException?.GetType().Name ?? nameof(InvalidOperationException),
                cleanupException?.Message ?? "Routed event trace cleanup failed.",
                CleanupStateFailed));

            _traceTransitions.CompleteCleanup();
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

    private bool TryScheduleDeferredHandlerRemoval(ActiveTraceSession session, Dispatcher? dispatcher)
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
                        RecordDeferredCleanupCompletion(session.Metadata, null);
                    }
                    catch (Exception ex)
                    {
                        LogCleanupFailure(ex);
                        RecordDeferredCleanupCompletion(session.Metadata, ex);
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
                System.Diagnostics.Trace.TraceWarning(
                    $"Timed out while removing routed event trace handlers: {SensitiveLogRedactor.Redact(timeoutException.Message)}");
                break;
            case InvalidOperationException invalidOperationException:
                System.Diagnostics.Trace.TraceWarning(
                    $"Skipped routed event trace handler removal because the dispatcher was unavailable during teardown: {SensitiveLogRedactor.Redact(invalidOperationException.Message)}");
                break;
            default:
                System.Diagnostics.Trace.TraceError(
                    $"Unexpected failure while removing routed event trace handlers during teardown: {SensitiveLogRedactor.Redact(exception.ToString())}");
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
}
