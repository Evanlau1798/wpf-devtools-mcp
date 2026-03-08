namespace WpfDevTools.Mcp.Server;

internal static class RateLimiterCleanupGuard
{
    public static void Execute(bool isDisposed, Action cleanupAction, Action<Exception> onError)
    {
        if (isDisposed)
        {
            return;
        }

        try
        {
            cleanupAction();
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}
