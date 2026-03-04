namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Manages active Inspector sessions
/// </summary>
public class SessionManager : IDisposable
{
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
    /// <param name="processId">Process ID</param>
    /// <returns>True if allowed, false if rate limit exceeded</returns>
    public bool CheckRateLimit(int processId)
    {
        return _rateLimiter.TryAcquire(processId);
    }

    /// <summary>
    /// Get available request tokens for monitoring
    /// </summary>
    public int GetAvailableTokens(int processId)
    {
        return _rateLimiter.GetAvailableTokens(processId);
    }

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

            _pipeClients[processId] = new NamedPipeClient(processId);
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
    /// Update last activity time for session by replacing with a new immutable instance
    /// </summary>
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
        public required DateTime LastActivity { get; init; }
    }

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
