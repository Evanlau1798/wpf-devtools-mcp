using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    internal const int MaxRetainedStateSnapshotsPerProcess = 20;
    internal static readonly TimeSpan StateSnapshotRetentionWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Check if session exists
    /// </summary>
    /// <param name="processId">Process ID to check</param>
    /// <returns>True if session exists, false otherwise</returns>
    public bool HasSession(int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _sessions.ContainsKey(processId);
        }
    }

    internal bool SaveStateSnapshot(
        int processId,
        StoredStateSnapshot snapshot,
        long? expectedSessionGeneration = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_lock)
        {
            if (!_sessionGenerations.TryGetValue(processId, out var currentSessionGeneration))
            {
                return false;
            }

            if (expectedSessionGeneration.HasValue && currentSessionGeneration != expectedSessionGeneration.Value)
            {
                return false;
            }

            if (!_stateSnapshots.TryGetValue(processId, out var snapshots))
            {
                snapshots = new Dictionary<string, StoredStateSnapshot>(StringComparer.Ordinal);
                _stateSnapshots[processId] = snapshots;
            }

            TrimStateSnapshotsLocked(processId, snapshots, _utcNowProvider());
            snapshots[snapshot.SnapshotId] = snapshot.SessionGeneration == currentSessionGeneration
                ? snapshot
                : snapshot with { SessionGeneration = currentSessionGeneration };
            TrimStateSnapshotsLocked(processId, snapshots, _utcNowProvider(), snapshot.SnapshotId);
            return true;
        }
    }

    internal bool TryGetStateSnapshot(int processId, string snapshotId, out StoredStateSnapshot? snapshot)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (!_sessionGenerations.TryGetValue(processId, out var currentSessionGeneration))
            {
                snapshot = null;
                return false;
            }

            if (_stateSnapshots.TryGetValue(processId, out var snapshots))
            {
                TrimStateSnapshotsLocked(processId, snapshots, _utcNowProvider());

                if (snapshots.TryGetValue(snapshotId, out var storedSnapshot))
                {
                    if (storedSnapshot.SessionGeneration != currentSessionGeneration)
                    {
                        RemoveStateSnapshotLocked(processId, snapshots, snapshotId);
                        snapshot = null;
                        return false;
                    }

                    snapshot = storedSnapshot;
                    return true;
                }
            }
        }

        snapshot = null;
        return false;
    }

    internal bool RemoveStateSnapshot(int processId, string snapshotId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _stateSnapshots.TryGetValue(processId, out var snapshots) &&
                   RemoveStateSnapshotLocked(processId, snapshots, snapshotId);
        }
    }

    private void TrimStateSnapshotsLocked(
        int processId,
        Dictionary<string, StoredStateSnapshot> snapshots,
        DateTimeOffset now,
        string? retainedSnapshotId = null)
    {
        var expirationCutoff = now - StateSnapshotRetentionWindow;
        foreach (var snapshotId in snapshots
            .Where(entry => !string.Equals(entry.Key, retainedSnapshotId, StringComparison.Ordinal)
                && entry.Value.CapturedAtUtc < expirationCutoff)
            .Select(entry => entry.Key)
            .ToArray())
        {
            RemoveStateSnapshotLocked(processId, snapshots, snapshotId);
        }

        while (snapshots.Count > MaxRetainedStateSnapshotsPerProcess)
        {
            var oldestSnapshotId = snapshots
                .Where(entry => !string.Equals(entry.Key, retainedSnapshotId, StringComparison.Ordinal))
                .OrderBy(entry => entry.Value.CapturedAtUtc)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .First()
                .Key;
            RemoveStateSnapshotLocked(processId, snapshots, oldestSnapshotId);
        }
    }

    private void TrimStateSnapshotsForProcessLocked(int processId)
    {
        if (_stateSnapshots.TryGetValue(processId, out var snapshots))
        {
            TrimStateSnapshotsLocked(processId, snapshots, _utcNowProvider());
        }
    }

    private bool RemoveStateSnapshotLocked(
        int processId,
        Dictionary<string, StoredStateSnapshot> snapshots,
        string snapshotId)
    {
        if (!snapshots.Remove(snapshotId))
        {
            return false;
        }

        if (_navigationStateStore.TryGetState(processId, out var state) &&
            string.Equals(state?.ActiveSnapshotId, snapshotId, StringComparison.Ordinal))
        {
            _navigationStateStore.SetActiveSnapshotId(processId, null);
        }

        return true;
    }

    /// <summary>
    /// Get count of active sessions
    /// </summary>
    /// <returns>Number of active sessions</returns>
    public int GetActiveSessionCount()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _sessions.Count;
        }
    }

    /// <summary>
    /// Get all active session process IDs
    /// </summary>
    /// <returns>Read-only list of process IDs for all active sessions</returns>
    public IReadOnlyList<int> GetAllSessions()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _sessions.Keys.ToList();
        }
    }

    /// <summary>
    /// Mark an existing connected session as the active process for process-id omission workflows.
    /// </summary>
    /// <param name="processId">Connected process ID to mark active.</param>
    public void SetActiveProcess(int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (!_sessions.ContainsKey(processId))
            {
                throw new InvalidOperationException($"Process {processId} is not connected. Connect first or choose an existing session.");
            }

            _activeProcessSelection = new ActiveProcessSelection
            {
                ProcessId = processId,
                SelectedAtUtc = _utcNowProvider()
            };
        }
    }

    internal bool TryActivateConnectedSession(int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (!_sessions.ContainsKey(processId) ||
                !_pipeClients.TryGetValue(processId, out var pipeClient) ||
                !pipeClient.IsConnected)
            {
                return false;
            }

            _activeProcessSelection = new ActiveProcessSelection
            {
                ProcessId = processId,
                SelectedAtUtc = _utcNowProvider()
            };

            return true;
        }
    }

    /// <summary>
    /// Try to get the active process ID for process-id omission workflows.
    /// </summary>
    /// <param name="processId">The active process ID when available.</param>
    /// <returns>True when an active process is selected; otherwise false.</returns>
    public bool TryGetActiveProcessId(out int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_activeProcessSelection != null)
            {
                processId = _activeProcessSelection.ProcessId;
                return true;
            }
        }

        processId = default;
        return false;
    }

    /// <summary>
    /// Try to get the full active-process selection state.
    /// </summary>
    /// <param name="selection">Selection payload when available.</param>
    /// <returns>True when an active process is selected; otherwise false.</returns>
    internal bool TryGetActiveProcessSelection(out ActiveProcessSelection? selection)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_activeProcessSelection != null)
            {
                selection = _activeProcessSelection;
                return true;
            }
        }

        selection = null;
        return false;
    }

    /// <summary>
    /// Update last activity time for session by replacing with a new immutable instance
    /// </summary>
    /// <param name="processId">Process ID of session to update</param>
    public void UpdateLastActivity(int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_sessions.TryGetValue(processId, out var session))
            {
                _sessions[processId] = new SessionInfo
                {
                    ProcessId = session.ProcessId,
                    LastActivity = _utcNowProvider()
                };
            }
        }
    }

    /// <summary>
    /// Get last activity time for session
    /// </summary>
    /// <param name="processId">Process ID to get last activity time for</param>
    /// <returns>Last activity time in UTC, or DateTimeOffset.MinValue if session does not exist</returns>
    public DateTimeOffset GetLastActivityTime(int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _sessions.TryGetValue(processId, out var session)
                ? session.LastActivity
                : DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Get sessions that have been idle for longer than specified timeout
    /// </summary>
    /// <param name="idleTimeout">Idle timeout duration</param>
    /// <returns>Read-only list of process IDs for idle sessions</returns>
    public IReadOnlyList<int> GetIdleSessions(TimeSpan idleTimeout)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            var now = _utcNowProvider();
            return _sessions
                .Where(kvp => now - kvp.Value.LastActivity > idleTimeout)
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }
}
