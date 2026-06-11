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

    internal bool TrySetActiveSnapshotId(int processId, string snapshotId, long expectedSessionGeneration)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (!_sessionGenerations.TryGetValue(processId, out var currentSessionGeneration) ||
                currentSessionGeneration != expectedSessionGeneration)
            {
                return false;
            }

            if (!_stateSnapshots.TryGetValue(processId, out var retainedSnapshots) ||
                !retainedSnapshots.ContainsKey(snapshotId))
            {
                return false;
            }

            _navigationStateStore.SetActiveSnapshotId(processId, snapshotId);
            return true;
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
        lock (_lock)
        {
            TrimStateSnapshotsForProcessLocked(processId);
            ClearMissingActiveSnapshotIdLocked(processId);
            return _navigationStateStore.TryGetState(processId, out state);
        }
    }

    internal bool TryGetActiveSnapshotId(int processId, out string? snapshotId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            TrimStateSnapshotsForProcessLocked(processId);

            if (_navigationStateStore.TryGetState(processId, out var state)
                && !string.IsNullOrWhiteSpace(state?.ActiveSnapshotId))
            {
                if (ClearMissingActiveSnapshotIdLocked(processId, state.ActiveSnapshotId))
                {
                    snapshotId = null;
                    return false;
                }

                snapshotId = state.ActiveSnapshotId;
                return true;
            }
        }

        snapshotId = null;
        return false;
    }

    private bool ClearMissingActiveSnapshotIdLocked(int processId)
    {
        if (!_navigationStateStore.TryGetState(processId, out var state) ||
            string.IsNullOrWhiteSpace(state?.ActiveSnapshotId))
        {
            return false;
        }

        return ClearMissingActiveSnapshotIdLocked(processId, state.ActiveSnapshotId);
    }

    private bool ClearMissingActiveSnapshotIdLocked(int processId, string snapshotId)
    {
        if (_stateSnapshots.TryGetValue(processId, out var retainedSnapshots) &&
            retainedSnapshots.ContainsKey(snapshotId))
        {
            return false;
        }

        _navigationStateStore.SetActiveSnapshotId(processId, null);
        return true;
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
