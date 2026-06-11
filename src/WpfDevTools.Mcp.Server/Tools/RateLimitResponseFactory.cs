namespace WpfDevTools.Mcp.Server.Tools;

internal static class RateLimitResponseFactory
{
    public static object Create(RateLimitStatus status, string errorMessage)
    {
        var retryAfterSeconds = NormalizeRetryAfterSeconds(status.RetryAfter);

        return new
        {
            success = false,
            error = errorMessage,
            errorCode = "RateLimitExceeded",
            availableTokens = status.AvailableTokens,
            retryAfterSeconds,
            retryAfter = BuildHumanReadableRetryAfter(retryAfterSeconds)
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
