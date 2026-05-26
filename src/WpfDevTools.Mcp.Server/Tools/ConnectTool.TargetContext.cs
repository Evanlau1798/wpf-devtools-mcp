using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private object? CheckConnectRateLimit(int processId)
    {
        var rateLimitStatus = _sessionManager.CheckRateLimitStatus(processId);
        return rateLimitStatus.Allowed
            ? null
            : RateLimitResponseFactory.Create(
                rateLimitStatus,
                "Rate limit exceeded for connect operations. Please slow down your requests.");
    }

    private sealed record ConnectTargetContext(
        WpfProcessInfo ProcessInfo,
        ProcessConnectionAccess Access,
        bool LikelySdkOnlyPackaging,
        bool IsRawInjectionTargetAllowed);
}
