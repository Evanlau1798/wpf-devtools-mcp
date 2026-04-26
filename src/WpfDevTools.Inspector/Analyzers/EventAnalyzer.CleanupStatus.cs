namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class EventAnalyzer
{
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
            cleanupState = "deferredPending",
            cleanupFailureMessage = previousCleanupException.Message,
            cleanupFailureType = previousCleanupException.GetType().Name
        };
    }
}
