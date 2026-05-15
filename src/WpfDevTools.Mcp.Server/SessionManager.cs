using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using WpfDevTools.Shared.Security;
using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Manages active Inspector sessions
/// </summary>
public sealed partial class SessionManager : IDisposable
{
    private static readonly TimeSpan ExistingHostAuthenticationFallbackTimeout = TimeSpan.FromSeconds(1);

    private int _disposeState;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly Dictionary<int, SessionInfo> _sessions = new();
    private readonly Dictionary<int, NamedPipeClient> _pipeClients = new();
    private readonly Dictionary<int, Dictionary<string, StoredStateSnapshot>> _stateSnapshots = new();
    private readonly Dictionary<int, long> _sessionGenerations = new();
    private ActiveProcessSelection? _activeProcessSelection;
    private readonly IRateLimiterManager _rateLimiter;
    private readonly AuthenticationManager? _authManager;
    private readonly ProcessAuthenticationSecretProvider _processAuthenticationSecrets;
    private readonly CertificateManager? _certManager;
    private readonly ILogger? _logger;
    private readonly object _lock = new();
    private readonly object _shutdownGuard = new();
    internal readonly System.Threading.Timer _cleanupTimer;
    private long _nextSessionGeneration;

    /// <summary>
    /// Create a new SessionManager with dependency injection
    /// </summary>
    /// <param name="rateLimiter">Rate limiter manager for controlling request rates</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for encryption (null to disable encryption)</param>
    /// <param name="logger">Logger for cleanup diagnostics (null to fall back to Debug.WriteLine)</param>
    public SessionManager(
        IRateLimiterManager rateLimiter,
        AuthenticationManager? authManager = null,
        CertificateManager? certManager = null,
        ILogger<SessionManager>? logger = null)
        : this(rateLimiter, authManager, certManager, logger, utcNowProvider: null)
    {
    }

    /// <summary>
    /// Create a new SessionManager with dependency injection and an optional deterministic UTC clock.
    /// </summary>
    /// <param name="rateLimiter">Rate limiter manager for controlling request rates</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for encryption (null to disable encryption)</param>
    /// <param name="logger">Logger for cleanup diagnostics (null to fall back to Debug.WriteLine)</param>
    /// <param name="utcNowProvider">Optional UTC clock provider for deterministic tests</param>
    internal SessionManager(
        IRateLimiterManager rateLimiter,
        AuthenticationManager? authManager,
        CertificateManager? certManager,
        ILogger<SessionManager>? logger,
        Func<DateTimeOffset>? utcNowProvider)
    {
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _authManager = authManager;
        _processAuthenticationSecrets = new ProcessAuthenticationSecretProvider(authManager);
        _certManager = certManager;
        _logger = logger;
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);

        // CRITICAL FIX: Periodic cleanup of dead and idle sessions
        // Uses one-shot timer (Infinite period) to prevent overlapping callbacks.
        // Timer is rescheduled at the end of PerformCleanup.
        var cleanupTimerState = new CleanupTimerState(this);
        _cleanupTimer = new System.Threading.Timer(
            callback: static state => CleanupTimerState.Invoke(state),
            state: cleanupTimerState,
            dueTime: McpServerConfiguration.SessionCleanupInterval,
            period: Timeout.InfiniteTimeSpan);
        cleanupTimerState.SetTimer(_cleanupTimer);
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
        : this(maxRequestsPerMinute, authManager, certManager, utcNowProvider: null)
    {
    }

    /// <summary>
    /// Create a new SessionManager with a deterministic UTC clock for tests.
    /// </summary>
    /// <param name="maxRequestsPerMinute">Maximum requests per minute per session (default from McpServerConfiguration)</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for encryption (null to disable encryption)</param>
    /// <param name="utcNowProvider">Optional UTC clock provider for deterministic tests</param>
    internal SessionManager(
        int maxRequestsPerMinute,
        AuthenticationManager? authManager,
        CertificateManager? certManager,
        Func<DateTimeOffset>? utcNowProvider)
        : this(new RateLimiterManager(maxRequestsPerMinute), authManager, certManager, logger: null, utcNowProvider: utcNowProvider)
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
        return CheckRateLimitStatus(processId).Allowed;
    }

    /// <summary>
    /// Attempt to acquire a rate-limit token and return the resulting snapshot.
    /// </summary>
    public RateLimitStatus CheckRateLimitStatus(int processId)
    {
        ThrowIfDisposed();

        if (_rateLimiter is IRateLimiterStatusProvider provider)
        {
            return provider.TryAcquireWithStatus(processId);
        }

        var allowed = _rateLimiter.TryAcquire(processId);
        var availableTokens = _rateLimiter.GetAvailableTokens(processId);
        var retryAfter = allowed ? TimeSpan.Zero : _rateLimiter.GetRetryAfter(processId);
        return new RateLimitStatus(allowed, availableTokens, retryAfter);
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
    /// Get the remaining wait time until the session can issue another request.
    /// Returns TimeSpan.Zero when the rate limiter would currently allow the request.
    /// </summary>
    public TimeSpan GetRetryAfter(int processId)
    {
        ThrowIfDisposed();
        return _rateLimiter.GetRetryAfter(processId);
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

            InitializeSessionState(processId, CreateProcessScopedPipeClient(processId));
        }
    }

    /// <summary>
    /// Remove a session
    /// </summary>
    /// <param name="processId">Process ID of session to remove</param>
    public void RemoveSession(int processId)
    {
        ThrowIfDisposed();
        NamedPipeClient? clientToDispose = null;

        lock (_lock)
        {
            _sessions.Remove(processId);
            if (_pipeClients.TryGetValue(processId, out var client))
            {
                clientToDispose = client;
                _pipeClients.Remove(processId);
            }

            _stateSnapshots.Remove(processId);
            _pendingEventReplay.Remove(processId);
            _pendingEventReplayLocks.Remove(processId);
            _sessionGenerations.Remove(processId);
            _navigationStateStore.RemoveProcess(processId);

            if (_activeProcessSelection?.ProcessId == processId)
            {
                _activeProcessSelection = null;
            }

            // Clean up rate limiter state
            _rateLimiter.RemoveSession(processId);
        }

        clientToDispose?.Dispose();
    }

    private void InitializeSessionState(int processId, NamedPipeClient pipeClient)
    {
        _sessions[processId] = new SessionInfo
        {
            ProcessId = processId,
            LastActivity = _utcNowProvider()
        };
        _sessionGenerations[processId] = ++_nextSessionGeneration;

        _pipeClients[processId] = pipeClient;
        _stateSnapshots[processId] = new Dictionary<string, StoredStateSnapshot>(StringComparer.Ordinal);
        _navigationStateStore.EnsureProcess(processId);
        _activeProcessSelection ??= new ActiveProcessSelection
        {
            ProcessId = processId,
            SelectedAtUtc = _utcNowProvider()
        };
    }














    private enum ExistingHostAuthenticationMode
    {
        ProcessScoped,
        Root
    }


    internal T ExecuteWithShutdownGuard<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_shutdownGuard)
        {
            ThrowIfDisposed();
            return action();
        }
    }















    private sealed record SessionInfo
    {
        public required int ProcessId { get; init; }
        public required DateTimeOffset LastActivity { get; init; }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
            throw new ObjectDisposedException(nameof(SessionManager));
    }

    private sealed class CleanupTimerState
    {
        private readonly WeakReference<SessionManager> _sessionManager;
        private System.Threading.Timer? _timer;

        public CleanupTimerState(SessionManager sessionManager)
        {
            _sessionManager = new WeakReference<SessionManager>(sessionManager);
        }

        public void SetTimer(System.Threading.Timer timer)
        {
            _timer = timer;
        }

        public static void Invoke(object? state)
        {
            if (state is not CleanupTimerState cleanupTimerState)
            {
                return;
            }

            if (cleanupTimerState._sessionManager.TryGetTarget(out var sessionManager))
            {
                sessionManager.PerformCleanup();
                return;
            }

            cleanupTimerState._timer?.Dispose();
        }
    }

    /// <summary>
    /// Dispose the session manager and release all resources
    /// </summary>
    public void Dispose()
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        lock (_shutdownGuard)
        {
            List<NamedPipeClient> clientsToDispose;

            lock (_lock)
            {
                if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
                    return;

                // Dispose cleanup timer
                _cleanupTimer.Dispose();

                // Dispose rate limiter manager
                (_rateLimiter as IDisposable)?.Dispose();

                clientsToDispose = _pipeClients.Values.ToList();

                _pipeClients.Clear();
                _sessions.Clear();
                _stateSnapshots.Clear();
                _pendingEventReplay.Clear();
                _navigationStateStore.Clear();
            }

            foreach (var client in clientsToDispose)
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
        }
    }
}
