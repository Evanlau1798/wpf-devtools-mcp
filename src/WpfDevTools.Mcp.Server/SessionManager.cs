namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Manages active Inspector sessions
/// </summary>
public class SessionManager
{
    private readonly Dictionary<int, SessionInfo> _sessions = new();
    private readonly object _lock = new();

    /// <summary>
    /// Add a new session
    /// </summary>
    public void AddSession(int processId)
    {
        lock (_lock)
        {
            if (_sessions.ContainsKey(processId))
            {
                throw new InvalidOperationException($"Session for process {processId} already exists");
            }

            _sessions[processId] = new SessionInfo
            {
                ProcessId = processId,
                LastActivity = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Remove a session
    /// </summary>
    public void RemoveSession(int processId)
    {
        lock (_lock)
        {
            _sessions.Remove(processId);
        }
    }

    /// <summary>
    /// Check if session exists
    /// </summary>
    public bool HasSession(int processId)
    {
        lock (_lock)
        {
            return _sessions.ContainsKey(processId);
        }
    }

    /// <summary>
    /// Get count of active sessions
    /// </summary>
    public int GetActiveSessionCount()
    {
        lock (_lock)
        {
            return _sessions.Count;
        }
    }

    /// <summary>
    /// Get all active session process IDs
    /// </summary>
    public IReadOnlyList<int> GetAllSessions()
    {
        lock (_lock)
        {
            return _sessions.Keys.ToList();
        }
    }

    /// <summary>
    /// Update last activity time for session
    /// </summary>
    public void UpdateLastActivity(int processId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(processId, out var session))
            {
                session.LastActivity = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Get last activity time for session
    /// </summary>
    public DateTime GetLastActivityTime(int processId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(processId, out var session)
                ? session.LastActivity
                : DateTime.MinValue;
        }
    }

    /// <summary>
    /// Get sessions that have been idle for longer than specified timeout
    /// </summary>
    public IReadOnlyList<int> GetIdleSessions(TimeSpan idleTimeout)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            return _sessions
                .Where(kvp => now - kvp.Value.LastActivity > idleTimeout)
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }

    private class SessionInfo
    {
        public required int ProcessId { get; init; }
        public DateTime LastActivity { get; set; }
    }
}
