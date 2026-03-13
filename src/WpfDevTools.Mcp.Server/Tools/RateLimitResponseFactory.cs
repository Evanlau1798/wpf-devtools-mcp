namespace WpfDevTools.Mcp.Server.Tools;

internal static class RateLimitResponseFactory
{
    public static object Create(SessionManager sessionManager, int processId, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);

        var availableTokens = sessionManager.GetAvailableTokens(processId);
        var retryAfter = sessionManager.GetRetryAfter(processId);
        var retryAfterSeconds = NormalizeRetryAfterSeconds(retryAfter);

        return new
        {
            success = false,
            error = errorMessage,
            availableTokens,
            retryAfterSeconds,
            retryAfter = BuildHumanReadableRetryAfter(retryAfterSeconds)
        };
    }

    private static int NormalizeRetryAfterSeconds(TimeSpan retryAfter)
    {
        if (retryAfter <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Ceiling(retryAfter.TotalSeconds);
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
