namespace WpfDevTools.Mcp.Server.Tools;

internal readonly record struct ProcessConnectionAccess(
    bool RequiresElevationToConnect,
    bool CanConnectFromCurrentServer,
    string? ConnectionWarning);

internal static class ProcessConnectionAccessEvaluator
{
    internal static ProcessConnectionAccess Evaluate(int processId, bool targetIsElevated, bool currentProcessIsElevated)
    {
        var requiresElevationToConnect = targetIsElevated && !currentProcessIsElevated;
        return new ProcessConnectionAccess(
            RequiresElevationToConnect: requiresElevationToConnect,
            CanConnectFromCurrentServer: !requiresElevationToConnect,
            ConnectionWarning: requiresElevationToConnect
                ? $"Target process {processId} is elevated. Restart the MCP server as administrator to connect or control this WPF process."
                : null);
    }
}
