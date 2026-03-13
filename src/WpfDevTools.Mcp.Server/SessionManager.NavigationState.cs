using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private readonly SessionNavigationStateStore _navigationStateStore = new();

    internal void SetActiveSnapshotId(int processId, string snapshotId)
    {
        ThrowIfDisposed();
        EnsureSessionExists(processId);
        _navigationStateStore.SetActiveSnapshotId(processId, snapshotId);
    }

    internal void ClearActiveSnapshotId(int processId)
    {
        ThrowIfDisposed();
        EnsureSessionExists(processId);
        _navigationStateStore.SetActiveSnapshotId(processId, null);
    }

    internal void SetActiveTraceState(int processId, ActiveTraceNavigationState traceState)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(traceState);
        EnsureSessionExists(processId);
        _navigationStateStore.SetActiveTrace(processId, traceState);
    }

    internal void ClearActiveTraceState(int processId)
    {
        ThrowIfDisposed();
        EnsureSessionExists(processId);
        _navigationStateStore.SetActiveTrace(processId, null);
    }

    internal bool TryGetNavigationState(int processId, out NavigationSessionState? state)
    {
        ThrowIfDisposed();
        return _navigationStateStore.TryGetState(processId, out state);
    }

    private void EnsureSessionExists(int processId)
    {
        lock (_lock)
        {
            if (!_sessions.ContainsKey(processId))
            {
                throw new InvalidOperationException($"Process {processId} is not connected. Connect first or choose an existing session.");
            }
        }
    }
}
