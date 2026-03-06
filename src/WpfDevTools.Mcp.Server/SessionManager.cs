using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Manages active Inspector sessions
/// </summary>
public sealed class SessionManager : IDisposable
{
    private volatile bool _isDisposed;
    private readonly Dictionary<int, SessionInfo> _sessions = new();
    private readonly Dictionary<int, NamedPipeClient> _pipeClients = new();
    private readonly IRateLimiterManager _rateLimiter;
    private readonly AuthenticationManager? _authManager;
    private readonly CertificateManager? _certManager;
    private readonly object _lock = new();
    private readonly System.Threading.Timer _cleanupTimer;

    /// <summary>
    /// Create a new SessionManager with dependency injection
    /// </summary>
    /// <param name="rateLimiter">Rate limiter manager for controlling request rates</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for encryption (null to disable encryption)</param>
    public SessionManager(
        IRateLimiterManager rateLimiter,
        AuthenticationManager? authManager = null,
        CertificateManager? certManager = null)
    {
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _authManager = authManager;
        _certManager = certManager;

        // CRITICAL FIX: Periodic cleanup of dead and idle sessions
        _cleanupTimer = new System.Threading.Timer(
            callback: _ => PerformCleanup(),
            state: null,
            dueTime: McpServerConfiguration.SessionCleanupInterval,
            period: McpServerConfiguration.SessionCleanupInterval);
    }

    /// <summary>
    /// Create a new SessionManager (backward compatibility constructor)
    /// </summary>
    /// <param name="maxRequestsPerMinute">Maximum requests per minute per session (default from McpServerConfiguration)</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for encryption (null to disable encryption)</param>
    public SessionManager(
        int maxRequestsPerMinute = McpServerConfiguration.RateLimitRequestsPerMinute,
        AuthenticationManager? authManager = null,
        CertificateManager? certManager = null)
        : this(new RateLimiterManager(maxRequestsPerMinute), authManager, certManager)
    {
    }

    /// <summary>
    /// Check if request is allowed under rate limit
    /// </summary>
    /// <param name="processId">Process ID to check rate limit for</param>
    /// <returns>True if request is allowed, false if rate limit exceeded</returns>
    /// <exception cref="ObjectDisposedException">Thrown when session manager has been disposed</exception>
    public bool CheckRateLimit(int processId)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_sessions.Count >= McpServerConfiguration.MaxSessions)
            {
                throw new InvalidOperationException($"Maximum session limit ({McpServerConfiguration.MaxSessions}) reached. Remove existing sessions before adding new ones.");
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

            _pipeClients[processId] = new NamedPipeClient(processId, _authManager, _certManager);
        }
    }

    /// <summary>
    /// Remove a session
    /// </summary>
    /// <param name="processId">Process ID of session to remove</param>
    public void RemoveSession(int processId)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
    /// Perform cleanup of both dead and idle sessions
    /// </summary>
    private void PerformCleanup()
    {
        if (_isDisposed) return;

        try
        {
            // Clean up dead sessions
            CleanupDeadSessions();

            // Clean up idle sessions
            var idleSessions = GetIdleSessions(McpServerConfiguration.SessionIdleTimeout);
            foreach (var processId in idleSessions)
            {
                RemoveSession(processId);
            }
        }
        catch (Exception ex)
        {
            // CRITICAL FIX: Log cleanup failures to file (STDIO-safe)
            // Cannot use ILogger here as it may not be available in timer callback
            // Prevent Timer callback exceptions from stopping future cleanup cycles.
            // STDIO MCP servers should not write to Console (stderr is technically safe
            // but we avoid it for consistency). Errors are swallowed since this is a
            // background cleanup operation - individual session cleanup failures are
            // non-critical.
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_SessionManager_Cleanup_{DateTime.UtcNow:yyyyMMdd}.log");
                File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] Cleanup error: {ex}\n");
            }
            catch (Exception logEx)
            {
                // Last resort: write to debug output to prevent timer crash
                System.Diagnostics.Debug.WriteLine($"SessionManager: Failed to log cleanup error: {logEx.Message}");
            }
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

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SessionManager));
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

            // Dispose cleanup timer
            _cleanupTimer.Dispose();

            // Dispose rate limiter manager
            (_rateLimiter as IDisposable)?.Dispose();

            // Dispose all pipe clients
            foreach (var client in _pipeClients.Values)
            {
                try
                {
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SessionManager: Failed to dispose pipe client: {ex.Message}");
                }
            }

            _pipeClients.Clear();
            _sessions.Clear();
        }
    }
}
