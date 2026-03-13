namespace WpfDevTools.Mcp.Server.Navigation;

public sealed record NavigationSessionState(
    string? ActiveSnapshotId,
    ActiveTraceNavigationState? ActiveTrace);

public sealed record ActiveTraceNavigationState(
    string EventName,
    string? ElementId,
    DateTimeOffset StartedAtUtc);
