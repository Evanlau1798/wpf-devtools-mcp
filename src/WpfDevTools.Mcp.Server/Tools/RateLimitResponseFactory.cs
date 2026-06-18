namespace WpfDevTools.Mcp.Server.Tools;

internal static class RateLimitResponseFactory
{
    public static object Create(RateLimitStatus status, string errorMessage)
    {
        var retryAfterSeconds = NormalizeRetryAfterSeconds(status.RetryAfter);
        var retryAfterMs = NormalizeRetryAfterMilliseconds(status.RetryAfter);
        var retryAfter = BuildHumanReadableRetryAfter(retryAfterSeconds);

        return new
        {
            success = false,
            error = errorMessage,
            errorCode = "RateLimitExceeded",
            availableTokens = status.AvailableTokens,
            retryAfterSeconds,
            retryAfterMs,
            retryAfter,
            recovery = new
            {
                suggestedAction = retryAfter,
                availableTokens = status.AvailableTokens,
                retryAfterSeconds,
                retryAfterMs,
                retryAfter
            }
        };
    }

    public static object Create(SessionManager sessionManager, int processId, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);

        var status = new RateLimitStatus(
            Allowed: false,
            AvailableTokens: sessionManager.GetAvailableTokens(processId),
            RetryAfter: sessionManager.GetRetryAfter(processId));
        return Create(status, errorMessage);
    }

    private static int NormalizeRetryAfterSeconds(TimeSpan retryAfter)
    {
        if (retryAfter <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Ceiling(retryAfter.TotalSeconds);
    }

    private static int NormalizeRetryAfterMilliseconds(TimeSpan retryAfter)
    {
        if (retryAfter <= TimeSpan.Zero)
        {
            return 0;
        }

        var milliseconds = Math.Ceiling(retryAfter.TotalMilliseconds);
        return milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
    }

    private static string BuildHumanReadableRetryAfter(int retryAfterSeconds)
    {
        if (retryAfterSeconds <= 0)
        {
            return "Retry now";
        }

        return retryAfterSeconds == 1
            ? "Wait 1 second for rate limit to reset"
            : $"Wait {retryAfterSeconds} seconds for rate limit to reset";
    }
}
