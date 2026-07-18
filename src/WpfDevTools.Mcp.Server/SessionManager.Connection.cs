using System.Diagnostics;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
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

    internal NamedPipeClient CreateDetachedPipeClient(int processId, string? pipeName = null)
    {
        ThrowIfDisposed();
        return CreateProcessScopedPipeClient(processId, pipeName);
    }

    internal void ReplacePipeClientForTesting(int processId, NamedPipeClient replacement)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(replacement);

        NamedPipeClient? existingClient = null;
        lock (_lock)
        {
            if (!_sessions.ContainsKey(processId))
            {
                throw new InvalidOperationException($"Session for process {processId} does not exist");
            }

            if (_pipeClients.TryGetValue(processId, out var currentClient)
                && !ReferenceEquals(currentClient, replacement))
            {
                existingClient = currentClient;
            }

            _pipeClients[processId] = replacement;
        }

        existingClient?.Dispose();
    }

    private NamedPipeClient CreateProcessScopedPipeClient(int processId, string? pipeName = null)
    {
        var processAuthManager = _processAuthenticationSecrets.CreateAuthenticationManager(processId, pipeName);
        return new NamedPipeClient(
            processId,
            string.IsNullOrWhiteSpace(pipeName) ? $"WpfDevTools_{processId}" : pipeName,
            processAuthManager,
            _certManager,
            ownsAuthManager: processAuthManager != null,
            enforceHostCompatibilityValidation: true);
    }

    private NamedPipeClient CreateRootAuthenticatedPipeClient(int processId, string? pipeName = null)
    {
        return new NamedPipeClient(
            processId,
            string.IsNullOrWhiteSpace(pipeName) ? $"WpfDevTools_{processId}" : pipeName,
            _authManager,
            _certManager,
            enforceHostCompatibilityValidation: true);
    }

    internal async Task<NamedPipeConnectFailure> ConnectInjectedSessionAsync(
        int processId,
        string? pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        EnsureSessionSlotAvailable(processId);

        return await ConnectAndAttachSessionAsync(
            processId,
            timeout,
            cancellationToken,
            () => CreateDetachedPipeClient(processId, pipeName),
            selectAsActive: true).ConfigureAwait(false);
    }

    internal Task<NamedPipeConnectFailure> ConnectInjectedSessionAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return ConnectInjectedSessionAsync(processId, pipeName: null, timeout, cancellationToken);
    }

    internal async Task<NamedPipeConnectFailure> ConnectExistingHostSessionAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool preferRootAuthentication = false,
        bool selectAsActive = true)
    {
        return await ConnectExistingHostSessionAsync(
            processId,
            pipeName: null,
            timeout,
            cancellationToken,
            preferRootAuthentication,
            selectAsActive).ConfigureAwait(false);
    }

    internal async Task<NamedPipeConnectFailure> ConnectExistingHostSessionAsync(
        int processId,
        string? pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool preferRootAuthentication = false,
        bool selectAsActive = true)
    {
        ThrowIfDisposed();

        var stopwatch = Stopwatch.StartNew();
        var primaryAuthMode = preferRootAuthentication
            ? ExistingHostAuthenticationMode.Root
            : ExistingHostAuthenticationMode.ProcessScoped;
        var primaryFailure = await ConnectExistingHostSessionAsync(
            processId,
            pipeName,
            timeout,
            cancellationToken,
            primaryAuthMode,
            selectAsActive).ConfigureAwait(false);
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
            pipeName,
            fallbackTimeout,
            cancellationToken,
            alternateAuthMode,
            selectAsActive).ConfigureAwait(false);
        return alternateFailure == NamedPipeConnectFailure.Timeout
            ? primaryFailure
            : alternateFailure;
    }

    private async Task<NamedPipeConnectFailure> ConnectExistingHostSessionAsync(
        int processId,
        string? pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        ExistingHostAuthenticationMode authenticationMode,
        bool selectAsActive)
    {
        ThrowIfDisposed();

        return await ConnectAndAttachSessionAsync(
            processId,
            timeout,
            cancellationToken,
            () => authenticationMode == ExistingHostAuthenticationMode.ProcessScoped
                ? CreateProcessScopedPipeClient(processId, pipeName)
                : CreateRootAuthenticatedPipeClient(processId, pipeName),
            selectAsActive).ConfigureAwait(false);
    }

    private async Task<NamedPipeConnectFailure> ConnectAndAttachSessionAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<NamedPipeClient> pipeClientFactory,
        bool selectAsActive)
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
            if (selectAsActive)
            {
                SetActiveProcess(processId);
            }
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

    internal string? GetAuthenticationSecretBase64(int processId, string? pipeName = null)
    {
        ThrowIfDisposed();
        return _processAuthenticationSecrets.GetAuthenticationSecretBase64(processId, pipeName);
    }

    internal string? GetCertificateDirectory()
    {
        ThrowIfDisposed();
        return _certManager?.CertificateDirectory;
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
}
