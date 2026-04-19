using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private readonly SessionNavigationStateStore _navigationStateStore = new();

    internal void SetActiveSnapshotId(int processId, string snapshotId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            EnsureSessionExistsLocked(processId);
            _navigationStateStore.SetActiveSnapshotId(processId, snapshotId);
        }
    }

    internal void ClearActiveSnapshotId(int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            EnsureSessionExistsLocked(processId);
            _navigationStateStore.SetActiveSnapshotId(processId, null);
        }
    }

    internal void SetActiveTraceState(int processId, ActiveTraceNavigationState traceState)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(traceState);
        lock (_lock)
        {
            EnsureSessionExistsLocked(processId);
            _navigationStateStore.SetActiveTrace(processId, traceState);
        }
    }

    internal void ClearActiveTraceState(int processId, string? endedSessionId = null)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            EnsureSessionExistsLocked(processId);
            _navigationStateStore.SetActiveTrace(processId, null, endedSessionId);
        }
    }

    internal bool TryGetNavigationState(int processId, out NavigationSessionState? state)
    {
        ThrowIfDisposed();
        return _navigationStateStore.TryGetState(processId, out state);
    }

    internal bool TryGetActiveSnapshotId(int processId, out string? snapshotId)
    {
        ThrowIfDisposed();
        if (_navigationStateStore.TryGetState(processId, out var state)
            && !string.IsNullOrWhiteSpace(state?.ActiveSnapshotId))
        {
            snapshotId = state.ActiveSnapshotId;
            return true;
        }

        snapshotId = null;
        return false;
    }

    private void EnsureSessionExists(int processId)
    {
        lock (_lock)
        {
            EnsureSessionExistsLocked(processId);
        }
    }

    /// <summary>
    /// Must be called while holding _lock to avoid TOCTOU races.
    /// </summary>
    private void EnsureSessionExistsLocked(int processId)
    {
        if (!_sessions.ContainsKey(processId))
        {
            throw new InvalidOperationException($"Process {processId} is not connected. Connect first or choose an existing session.");
        }
    }
}
