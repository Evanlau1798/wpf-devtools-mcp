namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class EventAnalyzer
{
    private const string CleanupStateDeferredCompleted = "deferredCompleted";
    private const string CleanupStateDeferredFailed = "deferredFailed";
    private const string CleanupStateDeferredPending = "deferredPending";
    private const string CleanupStateFailed = "failed";

    private static object CreateTraceStartResult(
        string eventName,
        int duration,
        int registrationCount,
        Exception? previousCleanupException)
    {
        if (previousCleanupException == null)
        {
            return new
            {
                success = true,
                message = $"Started tracing '{eventName}' for {duration}ms",
                eventName,
                duration,
                registrationCount
            };
        }

        return new
        {
            success = true,
            message = $"Started tracing '{eventName}' for {duration}ms",
            eventName,
            duration,
            registrationCount,
            cleanupIncomplete = true,
            cleanupState = CleanupStateDeferredPending,
            cleanupFailureMessage = previousCleanupException.Message,
            cleanupFailureType = previousCleanupException.GetType().Name
        };
    }

    private void StoreTraceCleanupStatus(TraceCleanupFailure cleanupStatus)
    {
        _lastTraceCleanupFailure = cleanupStatus;
        if (!_traceCleanupStatuses.ContainsKey(cleanupStatus.SessionId))
        {
            _traceCleanupStatusOrder.Enqueue(cleanupStatus.SessionId);
        }

        _traceCleanupStatuses[cleanupStatus.SessionId] = cleanupStatus;

        while (_traceCleanupStatuses.Count > MaxCompletedTraceSnapshots && _traceCleanupStatusOrder.Count > 0)
        {
            var oldestSessionId = _traceCleanupStatusOrder.Dequeue();
            _traceCleanupStatuses.Remove(oldestSessionId);
        }
    }

    private void RemoveTraceCleanupStatus(string sessionId)
    {
        _traceCleanupStatuses.Remove(sessionId);
    }

    private void RecordDeferredCleanupCompletion(TraceSessionMetadata metadata, Exception? exception)
    {
        lock (_lock)
        {
            StoreTraceCleanupStatus(new TraceCleanupFailure(
                metadata.SessionId,
                metadata.EventName,
                exception?.GetType().Name,
                exception?.Message,
                exception == null ? CleanupStateDeferredCompleted : CleanupStateDeferredFailed));
        }
    }

    private static bool IsCleanupStatusFailed(TraceCleanupFailure? cleanupStatus) =>
        cleanupStatus != null
        && !string.Equals(cleanupStatus.State, CleanupStateDeferredCompleted, StringComparison.Ordinal);

    private static bool IsCleanupStatusIncomplete(TraceCleanupFailure? cleanupStatus) =>
        cleanupStatus != null
        && string.Equals(cleanupStatus.State, CleanupStateDeferredPending, StringComparison.Ordinal);
}
