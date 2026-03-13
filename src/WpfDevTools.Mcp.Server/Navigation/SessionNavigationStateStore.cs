namespace WpfDevTools.Mcp.Server.Navigation;

internal sealed class SessionNavigationStateStore
{
    private readonly Dictionary<int, NavigationSessionState> _states = new();
    private readonly object _lock = new();

    public void EnsureProcess(int processId)
    {
        lock (_lock)
        {
            _states.TryAdd(processId, new NavigationSessionState(null, null));
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

    public void SetActiveTrace(int processId, ActiveTraceNavigationState? traceState) =>
        UpdateState(processId, state => state with { ActiveTrace = traceState });

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
                : new NavigationSessionState(null, null);
            _states[processId] = updater(current);
        }
    }
}
