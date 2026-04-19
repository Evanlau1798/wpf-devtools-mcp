namespace WpfDevTools.Mcp.Server.Navigation;

public sealed record NavigationSessionState(
    string? ActiveSnapshotId,
    ActiveTraceNavigationState? ActiveTrace,
    string? LastEndedTraceSessionId = null,
    IReadOnlyList<string>? RecentlyEndedTraceSessionIds = null);

public sealed record ActiveTraceNavigationState(
    string EventName,
    string? ElementId,
    DateTimeOffset StartedAtUtc,
    TimeSpan EffectiveDuration = default,
    string? SessionId = null,
    bool IgnoreExpiry = false,
    DateTimeOffset? FollowUpExpiresAtUtc = null)
{
    public bool HasExpired(DateTimeOffset now)
    {
        if (FollowUpExpiresAtUtc.HasValue)
        {
            return FollowUpExpiresAtUtc.Value <= now;
        }

        return !IgnoreExpiry
            && EffectiveDuration > TimeSpan.Zero
            && StartedAtUtc.Add(EffectiveDuration) <= now;
    }
}
