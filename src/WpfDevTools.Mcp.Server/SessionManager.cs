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
    private static readonly TimeSpan ExistingHostAuthenticationFallbackTimeout = TimeSpan.FromMilliseconds(250);

    private int _disposeState;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly Dictionary<int, SessionInfo> _sessions = new();
    internal readonly Dictionary<int, NamedPipeClient> _pipeClients = new();
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

    internal NamedPipeClient? GetPipeClient(int processId, long expectedSessionGeneration)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (!_sessionGenerations.TryGetValue(processId, out var currentGeneration)
                || currentGeneration != expectedSessionGeneration)
            {
                return null;
            }

            return _pipeClients.TryGetValue(processId, out var client) ? client : null;
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

    internal NamedPipeClient CreateDetachedPipeClient(int processId)
    {
        ThrowIfDisposed();
        return CreateProcessScopedPipeClient(processId);
    }

    private NamedPipeClient CreateProcessScopedPipeClient(int processId)
    {
        var processAuthManager = _processAuthenticationSecrets.CreateAuthenticationManager(processId);
        return new NamedPipeClient(
            processId,
            $"WpfDevTools_{processId}",
            processAuthManager,
            _certManager,
            ownsAuthManager: processAuthManager != null);
    }

    private NamedPipeClient CreateRootAuthenticatedPipeClient(int processId)
    {
        return new NamedPipeClient(processId, _authManager, _certManager);
    }

    internal async Task<NamedPipeConnectFailure> ConnectInjectedSessionAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        EnsureSessionSlotAvailable(processId);

        return await ConnectAndAttachSessionAsync(
            processId,
            timeout,
            cancellationToken,
            () => CreateDetachedPipeClient(processId)).ConfigureAwait(false);
    }

    internal async Task<NamedPipeConnectFailure> ConnectExistingHostSessionAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool preferRootAuthentication = false)
    {
        ThrowIfDisposed();

        var stopwatch = Stopwatch.StartNew();
        var primaryAuthMode = preferRootAuthentication
            ? ExistingHostAuthenticationMode.Root
            : ExistingHostAuthenticationMode.ProcessScoped;
        var primaryFailure = await ConnectExistingHostSessionAsync(
            processId,
            timeout,
            cancellationToken,
            primaryAuthMode).ConfigureAwait(false);
        if (primaryFailure != NamedPipeConnectFailure.AuthenticationFailed || !_processAuthenticationSecrets.IsEnabled)
        {
            return primaryFailure;
        }

        var remainingTimeout = timeout - stopwatch.Elapsed;
        if (remainingTimeout <= TimeSpan.Zero)
        {
            return NamedPipeConnectFailure.Timeout;
        }

        var alternateAuthMode = primaryAuthMode == ExistingHostAuthenticationMode.ProcessScoped
            ? ExistingHostAuthenticationMode.Root
            : ExistingHostAuthenticationMode.ProcessScoped;
        var fallbackTimeout = remainingTimeout < ExistingHostAuthenticationFallbackTimeout
            ? remainingTimeout
            : ExistingHostAuthenticationFallbackTimeout;
        var alternateFailure = await ConnectExistingHostSessionAsync(
            processId,
            fallbackTimeout,
            cancellationToken,
            alternateAuthMode).ConfigureAwait(false);
        return alternateFailure == NamedPipeConnectFailure.Timeout
            ? primaryFailure
            : alternateFailure;
    }

    private async Task<NamedPipeConnectFailure> ConnectExistingHostSessionAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        ExistingHostAuthenticationMode authenticationMode)
    {
        ThrowIfDisposed();

        return await ConnectAndAttachSessionAsync(
            processId,
            timeout,
            cancellationToken,
            () => authenticationMode == ExistingHostAuthenticationMode.ProcessScoped
                ? CreateProcessScopedPipeClient(processId)
                : CreateRootAuthenticatedPipeClient(processId)).ConfigureAwait(false);
    }

    private async Task<NamedPipeConnectFailure> ConnectAndAttachSessionAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<NamedPipeClient> pipeClientFactory)
    {
        NamedPipeClient? detachedPipeClient = null;
        try
        {
            detachedPipeClient = pipeClientFactory();
            var connected = await detachedPipeClient.ConnectAsync(
                timeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!connected)
            {
                return detachedPipeClient.LastConnectFailure;
            }

            cancellationToken.ThrowIfCancellationRequested();
            AttachSession(processId, detachedPipeClient);
            detachedPipeClient = null;
            SetActiveProcess(processId);
            return NamedPipeConnectFailure.None;
        }
        finally
        {
            detachedPipeClient?.Dispose();
        }
    }

    internal void AttachSession(int processId, NamedPipeClient pipeClient)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(pipeClient);

        lock (_lock)
        {
            if (_sessions.Count >= McpServerConfiguration.MaxSessions)
            {
                throw new InvalidOperationException($"Maximum session limit ({McpServerConfiguration.MaxSessions}) reached. Remove existing sessions before adding new sessions.");
            }

            if (_sessions.ContainsKey(processId))
            {
                throw new InvalidOperationException($"Session for process {processId} already exists");
            }

            InitializeSessionState(processId, pipeClient);
        }
    }

    private void EnsureSessionSlotAvailable(int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_sessions.Count >= McpServerConfiguration.MaxSessions)
            {
                throw new InvalidOperationException($"Maximum session limit ({McpServerConfiguration.MaxSessions}) reached. Remove existing sessions before adding new sessions.");
            }

            if (_sessions.ContainsKey(processId))
            {
                throw new InvalidOperationException($"Session for process {processId} already exists");
            }
        }
    }

    internal string? GetAuthenticationSecretBase64(int processId)
    {
        ThrowIfDisposed();
        return _processAuthenticationSecrets.GetAuthenticationSecretBase64(processId);
    }

    private enum ExistingHostAuthenticationMode
    {
        ProcessScoped,
        Root
    }

    internal string? GetCertificateDirectory()
    {
        ThrowIfDisposed();
        return _certManager?.CertificateDirectory;
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

    /// <summary>
    /// Materialize the shared certificate artifacts required for secure bootstrap
    /// before the injector launches the inspector. This prevents the server and
    /// injected process from racing to create the same certificate files.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the certificate manager reports success but the expected on-disk
    /// artifacts are still missing.
    /// </exception>
    internal void EnsureSecureTransportArtifactsCreated()
    {
        ThrowIfDisposed();

        if (_certManager == null)
        {
            return;
        }

        using var certificate = _certManager.GetOrCreateCertificate();

        var certDirectory = _certManager.CertificateDirectory;
        if (!File.Exists(Path.Combine(certDirectory, "server.pfx")) ||
            !File.Exists(Path.Combine(certDirectory, "server.pwd")))
        {
            throw new InvalidOperationException($"Secure transport certificate artifacts were not created under '{certDirectory}'.");
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

    internal void SaveStateSnapshot(int processId, StoredStateSnapshot snapshot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_lock)
        {
            if (!_stateSnapshots.TryGetValue(processId, out var snapshots))
            {
                snapshots = new Dictionary<string, StoredStateSnapshot>(StringComparer.Ordinal);
                _stateSnapshots[processId] = snapshots;
            }

            snapshots[snapshot.SnapshotId] = snapshot;
        }
    }

    internal bool TryGetStateSnapshot(int processId, string snapshotId, out StoredStateSnapshot? snapshot)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_stateSnapshots.TryGetValue(processId, out var snapshots) &&
                snapshots.TryGetValue(snapshotId, out var storedSnapshot))
            {
                snapshot = storedSnapshot;
                return true;
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
                   snapshots.Remove(snapshotId);
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
