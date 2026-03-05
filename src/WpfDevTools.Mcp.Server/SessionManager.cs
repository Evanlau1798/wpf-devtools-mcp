namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Manages active Inspector sessions
/// </summary>
public class SessionManager : IDisposable
{
    private const int MaxSessions = 50;
    private bool _isDisposed;
    private readonly Dictionary<int, SessionInfo> _sessions = new();
    private readonly Dictionary<int, NamedPipeClient> _pipeClients = new();
    private readonly IRateLimiterManager _rateLimiter;
    private readonly object _lock = new();

    /// <summary>
    /// Create a new SessionManager with dependency injection
    /// </summary>
    /// <param name="rateLimiter">Rate limiter manager for controlling request rates</param>
    public SessionManager(IRateLimiterManager rateLimiter)
    {
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    }

    /// <summary>
    /// Create a new SessionManager (backward compatibility constructor)
    /// </summary>
    /// <param name="maxRequestsPerMinute">Maximum requests per minute per session (default: 100)</param>
    public SessionManager(int maxRequestsPerMinute = 100)
        : this(new RateLimiterManager(maxRequestsPerMinute))
    {
    }

    /// <summary>
    /// Check if request is allowed under rate limit
    /// </summary>
    /// <param name="processId">Process ID to check rate limit for</param>
    /// <returns>True if request is allowed, false if rate limit exceeded</returns>
    public bool CheckRateLimit(int processId)
    {
        return _rateLimiter.TryAcquire(processId);
    }

    /// <summary>
    /// Get available request tokens for monitoring
    /// </summary>
    /// <param name="processId">Process ID to check available tokens for</param>
    /// <returns>Number of available request tokens</returns>
    public int GetAvailableTokens(int processId)
    {
        return _rateLimiter.GetAvailableTokens(processId);
    }

    /// <summary>
    /// Add a new session
    /// </summary>
    /// <param name="processId">Process ID to create session for</param>
    /// <exception cref="InvalidOperationException">Thrown when maximum session limit is reached or session already exists</exception>
    public void AddSession(int processId)
    {
        lock (_lock)
        {
            if (_sessions.Count >= MaxSessions)
            {
                throw new InvalidOperationException($"Maximum session limit ({MaxSessions}) reached. Remove existing sessions before adding new ones.");
            }

            if (_sessions.ContainsKey(processId))
            {
                throw new InvalidOperationException($"Session for process {processId} already exists");
            }

            _sessions[processId] = new SessionInfo
            {
                ProcessId = processId,
                LastActivity = DateTime.UtcNow
            };

            _pipeClients[processId] = new NamedPipeClient(processId);
        }
    }

    /// <summary>
    /// Remove a session
    /// </summary>
    /// <param name="processId">Process ID of session to remove</param>
    public void RemoveSession(int processId)
    {
        lock (_lock)
        {
            _sessions.Remove(processId);
            if (_pipeClients.TryGetValue(processId, out var client))
            {
                client.Dispose();
                _pipeClients.Remove(processId);
            }

            // Clean up rate limiter state
            _rateLimiter.RemoveSession(processId);
        }
    }

    /// <summary>
    /// Get the NamedPipeClient for a given process
    /// </summary>
    /// <param name="processId">Process ID to get pipe client for</param>
    /// <returns>NamedPipeClient instance if session exists, null otherwise</returns>
    public NamedPipeClient? GetPipeClient(int processId)
    {
        lock (_lock)
        {
            return _pipeClients.TryGetValue(processId, out var client) ? client : null;
        }
    }

    /// <summary>
    /// Check if session exists
    /// </summary>
    /// <param name="processId">Process ID to check</param>
    /// <returns>True if session exists, false otherwise</returns>
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
    /// <returns>Number of active sessions</returns>
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
    /// <returns>Read-only list of process IDs for all active sessions</returns>
    public IReadOnlyList<int> GetAllSessions()
    {
        lock (_lock)
        {
            return _sessions.Keys.ToList();
        }
    }

    /// <summary>
    /// Update last activity time for session by replacing with a new immutable instance
    /// </summary>
    /// <param name="processId">Process ID of session to update</param>
    public void UpdateLastActivity(int processId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(processId, out var session))
            {
                _sessions[processId] = new SessionInfo
                {
                    ProcessId = session.ProcessId,
                    LastActivity = DateTime.UtcNow
                };
            }
        }
    }

    /// <summary>
    /// Get last activity time for session
    /// </summary>
    /// <param name="processId">Process ID to get last activity time for</param>
    /// <returns>Last activity time in UTC, or DateTime.MinValue if session does not exist</returns>
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
    /// <param name="idleTimeout">Idle timeout duration</param>
    /// <returns>Read-only list of process IDs for idle sessions</returns>
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

    /// <summary>
    /// Clean up sessions for processes that no longer exist
    /// CRITICAL FIX: Prevents memory leak from dead sessions
    /// </summary>
    private void CleanupDeadSessions()
    {
        List<int> deadProcessIds;

        lock (_lock)
        {
            deadProcessIds = new List<int>();

            foreach (var processId in _sessions.Keys)
            {
                if (!IsProcessAlive(processId))
                {
                    deadProcessIds.Add(processId);
                }
            }
        }

        // Remove dead sessions outside the lock to avoid holding lock during disposal
        foreach (var processId in deadProcessIds)
        {
            RemoveSession(processId);
        }
    }

    /// <summary>
    /// Check if a process is still running
    /// </summary>
    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            return false;
        }
        catch (InvalidOperationException)
        {
            // Process has exited
            return false;
        }
    }

    private class SessionInfo
    {
        public required int ProcessId { get; init; }
        public required DateTime LastActivity { get; init; }
    }

    /// <summary>
    /// Dispose the session manager and release all resources
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            // Dispose all pipe clients
            foreach (var client in _pipeClients.Values)
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }

            _pipeClients.Clear();
            _sessions.Clear();
        }
    }
}
