namespace WpfDevTools.Mcp.Server.Tools;

internal sealed record ProcessDiscoveryCandidateSummary(
    int ProcessId,
    string ProcessName,
    string? WindowTitle,
    long WorkingSetBytes,
    bool IsElevated,
    bool RequiresElevationToConnect,
    bool CanConnectFromCurrentServer,
    string? ConnectionWarning);
