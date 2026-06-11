using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

internal static class ConnectToolTestPolicies
{
    public static McpTargetAuthorization AllowAllTargets(WpfProcessInfo _)
        => new(IsAllowed: true, Error: null, Hint: null);
}
