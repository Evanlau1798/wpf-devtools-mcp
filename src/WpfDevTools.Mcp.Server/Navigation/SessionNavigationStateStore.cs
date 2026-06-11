namespace WpfDevTools.Mcp.Server.Navigation;

internal sealed class SessionNavigationStateStore
{
    private const int EndedTraceSessionHistoryLimit = 8;
    private readonly Dictionary<int, NavigationSessionState> _states = new();
    private readonly object _lock = new();

    public void EnsureProcess(int processId)
    {
        lock (_lock)
        {
            _states.TryAdd(processId, new NavigationSessionState(null, null, null, Array.Empty<string>()));
        }
    }

    public bool TryGetState(int processId, out NavigationSessionState? state)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(processId, out var existing))
            {
                state = existing;
                return true;
            }
        }

        state = null;
        return false;
    }

    public void SetActiveSnapshotId(int processId, string? snapshotId) =>
        UpdateState(processId, state => state with { ActiveSnapshotId = snapshotId });

    public void SetActiveTrace(int processId, ActiveTraceNavigationState? traceState, string? lastEndedTraceSessionId = null) =>
        UpdateState(
            processId,
            state => traceState is not null
                ? state with
                {
                    ActiveTrace = traceState,
                    LastEndedTraceSessionId = null
                }
                : state with
                {
                    ActiveTrace = null,
                    LastEndedTraceSessionId = lastEndedTraceSessionId,
                    RecentlyEndedTraceSessionIds = AppendEndedTraceSessionId(
                        state.RecentlyEndedTraceSessionIds,
                        lastEndedTraceSessionId)
                });

    public void RemoveProcess(int processId)
    {
        lock (_lock)
        {
            _states.Remove(processId);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _states.Clear();
        }
    }

    private void UpdateState(int processId, Func<NavigationSessionState, NavigationSessionState> updater)
    {
        lock (_lock)
        {
            var current = _states.TryGetValue(processId, out var existing)
                ? existing
                : new NavigationSessionState(null, null, null, Array.Empty<string>());
            _states[processId] = updater(current);
        }
    }

    private static IReadOnlyList<string> AppendEndedTraceSessionId(
        IReadOnlyList<string>? existingIds,
        string? endedSessionId)
    {
        if (string.IsNullOrWhiteSpace(endedSessionId))
        {
            return existingIds ?? Array.Empty<string>();
        }

        return new[] { endedSessionId }
            .Concat(existingIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(EndedTraceSessionHistoryLimit)
            .ToArray();
    }
}
