using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Named Pipe client for communicating with Inspector DLL
/// </summary>
internal enum NamedPipeConnectFailure
{
    None = 0,
    Timeout = 1,
    AuthenticationFailed = 2,
    SecureTransportFailed = 3,
    AccessDenied = 4,
    ServerProcessMismatch = 5,
    IncompatibleHost = 6
}

public sealed class NamedPipeClient : IDisposable
{
    private static readonly TimeSpan DisposeSemaphoreWaitTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions IpcSerializerOptions = new()
    {
        MaxDepth = 32
    };

    private readonly int _processId;
    private readonly string _pipeName;
    private readonly AuthenticationManager? _authManager;
    private readonly CertificateManager? _certManager;
    private readonly bool _enforceHostCompatibilityValidation;
    private readonly TimeSpan _requestTimeout;
    private NamedPipeClientStream? _pipeClient;
    private Stream? _communicationStream;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _pipeSemaphore = new(1, 1);
    private int _disposeState;
    private int _lastConnectFailure;
    private static readonly string CurrentBuildFingerprint =
        InspectorCompatibilityContract.GetBuildFingerprint(typeof(NamedPipeClient));

    /// <summary>
    /// Initializes a new instance of the NamedPipeClient class without authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    public NamedPipeClient(int processId)
        : this(processId, BuildPipeName(processId), null, null)
    {
    }

    internal NamedPipeClient(int processId, string pipeName)
        : this(processId, pipeName, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the NamedPipeClient class with optional authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    public NamedPipeClient(int processId, AuthenticationManager? authManager)
        : this(processId, BuildPipeName(processId), authManager, null)
    {
    }

    /// <summary>
    /// Initializes a new instance with optional authentication and encryption
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for SslStream encryption (null to disable encryption)</param>
    public NamedPipeClient(int processId, AuthenticationManager? authManager, CertificateManager? certManager)
        : this(processId, BuildPipeName(processId), authManager, certManager)
    {
    }

    internal NamedPipeClient(
        int processId,
        string pipeName,
        AuthenticationManager? authManager,
        CertificateManager? certManager,
        bool? enforceHostCompatibilityValidation = null,
        TimeSpan? requestTimeout = null)
    {
        _processId = processId;
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? BuildPipeName(processId)
            : pipeName;
        _authManager = authManager;
        _certManager = certManager;
        _requestTimeout = requestTimeout.HasValue && requestTimeout.Value > TimeSpan.Zero
            ? requestTimeout.Value
            : InspectorConfig.RequestTimeout;

        var usesDefaultPipeName = string.Equals(
            _pipeName,
            BuildPipeName(processId),
            StringComparison.Ordinal);
        _enforceHostCompatibilityValidation =
            enforceHostCompatibilityValidation ?? usesDefaultPipeName;
    }

    private static string BuildPipeName(int processId) => $"WpfDevTools_{processId}";

    /// <summary>
    /// Pipe name
    /// </summary>
    public string PipeName => _pipeName;

    internal NamedPipeConnectFailure LastConnectFailure =>
        (NamedPipeConnectFailure)Volatile.Read(ref _lastConnectFailure);

    /// <summary>
    /// Check if connected
    /// </summary>
    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _pipeClient?.IsConnected ?? false;
            }
        }
    }

    /// <summary>
    /// Connect to Inspector with timeout and retry
    /// </summary>
    /// <param name="timeout">Total timeout budget shared across all retry attempts</param>
    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="cancellationToken">External cancellation token to abort the entire connect operation</param>
    public async Task<bool> ConnectAsync(TimeSpan timeout, int maxRetries = 3, CancellationToken cancellationToken = default)
    {
        await _pipeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetLastConnectFailure(NamedPipeConnectFailure.None);
            var timeoutBudget = Stopwatch.StartNew();

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var remainingTimeout = timeout - timeoutBudget.Elapsed;
                if (remainingTimeout <= TimeSpan.Zero)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
                    return false;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // A reconnect must tear down both the previous pipe and any wrapped SslStream
                    // before publishing a new transport.
                    ResetConnectionState();

                    NamedPipeClientStream localClient;
                    lock (_lock)
                    {
                        if (Volatile.Read(ref _disposeState) != 0)
                            return false;

                        _pipeClient = new NamedPipeClientStream(
                            ".",
                            _pipeName,
                            PipeDirection.InOut,
                            PipeOptions.Asynchronous);
                        localClient = _pipeClient;
                    }

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(remainingTimeout);
                    await localClient.ConnectAsync(cts.Token).ConfigureAwait(false);

                    if (_authManager != null && _authManager.IsAuthenticationEnabled)
                    {
                        if (!await AuthenticateToInspectorAsync(localClient, cts.Token).ConfigureAwait(false))
                        {
                            ResetConnectionState();
                            return false;
                        }
                    }

                    if (_certManager != null)
                    {
                        var sslStream = await CreateClientSslStreamAsync(localClient, cts.Token).ConfigureAwait(false);
                        if (sslStream == null)
                        {
                            ResetConnectionState();
                            return false;
                        }

                        lock (_lock)
                        {
                            _communicationStream = sslStream;
                        }
                    }
                    else
                    {
                        lock (_lock)
                        {
                            _communicationStream = localClient;
                        }
                    }

                    if (Volatile.Read(ref _disposeState) != 0)
                    {
                        ResetConnectionState();
                        return false;
                    }

                    if (_enforceHostCompatibilityValidation)
                    {
                        var compatibilityFailure = await ValidateConnectedHostAsync(localClient, cts.Token).ConfigureAwait(false);
                        if (compatibilityFailure != NamedPipeConnectFailure.None)
                        {
                            SetLastConnectFailure(compatibilityFailure);
                            ResetConnectionState();
                            return false;
                        }
                    }

                    SetLastConnectFailure(NamedPipeConnectFailure.None);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.AccessDenied);
                    if (!await HandleConnectRetryAsync(attempt, maxRetries, timeout, timeoutBudget, cancellationToken).ConfigureAwait(false))
                        return false;
                }
                catch (IOException)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
                    if (!await HandleConnectRetryAsync(attempt, maxRetries, timeout, timeoutBudget, cancellationToken).ConfigureAwait(false))
                        return false;
                }
                catch (TimeoutException)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
                    if (!await HandleConnectRetryAsync(attempt, maxRetries, timeout, timeoutBudget, cancellationToken).ConfigureAwait(false))
                        return false;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ResetConnectionState();
                    throw;
                }
                catch (OperationCanceledException)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
                    if (!await HandleConnectRetryAsync(attempt, maxRetries, timeout, timeoutBudget, cancellationToken).ConfigureAwait(false))
                        return false;
                }
                catch (ObjectDisposedException)
                {
                    ResetConnectionState();
                    return false;
                }
            }

            SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
            return false;
        }
        finally
        {
            _pipeSemaphore.Release();
        }
    }

    private async Task<bool> HandleConnectRetryAsync(
        int attempt,
        int maxRetries,
        TimeSpan totalTimeout,
        Stopwatch timeoutBudget,
        CancellationToken cancellationToken)
    {
        ResetConnectionState();

        if (attempt == maxRetries)
            return false;

        var remainingTimeout = totalTimeout - timeoutBudget.Elapsed;
        if (remainingTimeout <= TimeSpan.Zero)
            return false;

        var retryDelay = remainingTimeout < TimeSpan.FromMilliseconds(500)
            ? remainingTimeout
            : TimeSpan.FromMilliseconds(500);
        if (retryDelay <= TimeSpan.Zero)
            return false;

        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void ResetConnectionState()
    {
        NamedPipeClientStream? pipeToDispose = null;
        Stream? streamToDispose = null;

        lock (_lock)
        {
            streamToDispose = _communicationStream;
            pipeToDispose = _pipeClient;
            _communicationStream = null;
            _pipeClient = null;
        }

        if (streamToDispose != null && !ReferenceEquals(streamToDispose, pipeToDispose))
        {
            try { streamToDispose.Dispose(); } catch (IOException) { }
        }

        try { pipeToDispose?.Dispose(); } catch (IOException) { }
    }

    private async Task<SslStream?> CreateClientSslStreamAsync(
        NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        SslStream? sslStream = null;
        try
        {
            var expectedThumbprint = GetExpectedServerThumbprint();
            sslStream = new SslStream(pipe, leaveInnerStreamOpen: true,
                (sender, cert, chain, errors) =>
                {
                    if (cert == null) return false;
                    using var cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);

                    // Verify subject name
                    if (cert2.Subject != "CN=WpfDevTools-Inspector")
                        return false;

                    // If thumbprint is configured, pin to that specific certificate
                    // If no thumbprint available, accept any cert with valid subject name (already checked above)
                    if (string.IsNullOrWhiteSpace(expectedThumbprint))
                        return true;

                    return string.Equals(cert2.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
                });

#if NET48
            await sslStream.AuthenticateAsClientAsync(
                "WpfDevTools-Inspector",
                null,
                SslProtocols.Tls12,
                checkCertificateRevocation: false);
#else
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "WpfDevTools-Inspector",
                EnabledSslProtocols = SslProtocols.Tls12,
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
            };
            await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);
#endif
            return sslStream;
        }
        catch (OperationCanceledException)
        {
            sslStream?.Dispose();
            SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
            return null;
        }
        catch (AuthenticationException)
        {
            sslStream?.Dispose();
            SetLastConnectFailure(NamedPipeConnectFailure.SecureTransportFailed);
            return null;
        }
        catch (IOException)
        {
            sslStream?.Dispose();
            SetLastConnectFailure(NamedPipeConnectFailure.SecureTransportFailed);
            return null;
        }
        catch (ObjectDisposedException)
        {
            sslStream?.Dispose();
            SetLastConnectFailure(NamedPipeConnectFailure.SecureTransportFailed);
            return null;
        }
    }

    private string? GetExpectedServerThumbprint()
    {
        var configuredThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_THUMBPRINT");
        if (!string.IsNullOrWhiteSpace(configuredThumbprint))
        {
            return configuredThumbprint;
        }

        using var cert = _certManager?.GetOrCreateCertificate();
        return cert?.Thumbprint;
    }

    private async Task<bool> AuthenticateToInspectorAsync(
        NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Read 32-byte challenge from server
            var challenge = new byte[32];
            var totalRead = 0;
            while (totalRead < 32)
            {
                var read = await pipe.ReadAsync(challenge, totalRead, 32 - totalRead, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.AuthenticationFailed);
                    return false;
                }
                totalRead += read;
            }

            // 2. Compute HMAC-SHA256 response
            // GetSharedSecret returns a clone; zero it after use to minimize secret exposure in memory
            var secretCopy = _authManager!.GetSharedSecret();
            byte[] response;
            try
            {
                using var calculator = new ResponseCalculator(secretCopy);
                response = calculator.ComputeResponse(challenge);
            }
            finally
            {
                Array.Clear(secretCopy, 0, secretCopy.Length);
            }

            // 3. Send response
            await pipe.WriteAsync(response, 0, response.Length, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            // 4. Read 1-byte result from server (1=success, 0=failure)
            var resultBuf = new byte[1];
            var resultRead = await pipe.ReadAsync(resultBuf, 0, 1, cancellationToken).ConfigureAwait(false);
            if (resultRead == 0)
            {
                SetLastConnectFailure(NamedPipeConnectFailure.AuthenticationFailed);
                return false;
            }

            var authenticated = resultBuf[0] == 1;
            if (!authenticated)
            {
                SetLastConnectFailure(NamedPipeConnectFailure.AuthenticationFailed);
            }

            return authenticated;
        }
        catch (OperationCanceledException)
        {
            SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
            return false;
        }
        catch (IOException)
        {
            SetLastConnectFailure(NamedPipeConnectFailure.AuthenticationFailed);
            return false;
        }
    }

    private void SetLastConnectFailure(NamedPipeConnectFailure failure)
    {
        Volatile.Write(ref _lastConnectFailure, (int)failure);
    }

    private async Task<NamedPipeConnectFailure> ValidateConnectedHostAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        var serverProcessId = TryGetConnectedServerProcessId(pipe);
        if (serverProcessId.HasValue && serverProcessId.Value != _processId && !IsSameProcessDefaultPipeHost(serverProcessId.Value))
        {
            return NamedPipeConnectFailure.ServerProcessMismatch;
        }

        try
        {
            var response = await SendRequestCoreAsync(
                "ping",
                $"connect-verify-{Guid.NewGuid():N}",
                new { },
                cancellationToken).ConfigureAwait(false);

            if (response.Error != null || response.Result is null)
            {
                return NamedPipeConnectFailure.IncompatibleHost;
            }

            var result = response.Result.Value;
            if (!TryGetInt32Property(result, "processId", out var hostProcessId) || hostProcessId != _processId)
            {
                return NamedPipeConnectFailure.ServerProcessMismatch;
            }

            if (!TryGetInt32Property(result, "protocolVersion", out var protocolVersion) ||
                protocolVersion != InspectorCompatibilityContract.ProtocolVersion)
            {
                return NamedPipeConnectFailure.IncompatibleHost;
            }

            if (!TryGetStringProperty(result, "buildFingerprint", out var buildFingerprint) ||
                !string.Equals(buildFingerprint, CurrentBuildFingerprint, StringComparison.Ordinal))
            {
                return NamedPipeConnectFailure.IncompatibleHost;
            }

            return NamedPipeConnectFailure.None;
        }
        catch (OperationCanceledException)
        {
            return NamedPipeConnectFailure.Timeout;
        }
        catch (IOException)
        {
            return NamedPipeConnectFailure.IncompatibleHost;
        }
        catch (ObjectDisposedException)
        {
            return NamedPipeConnectFailure.Timeout;
        }
        catch (InvalidOperationException)
        {
            return NamedPipeConnectFailure.IncompatibleHost;
        }
        catch (JsonException)
        {
            return NamedPipeConnectFailure.IncompatibleHost;
        }
    }

    private static bool TryGetInt32Property(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value);
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = property.GetString());
    }

    private bool IsSameProcessDefaultPipeHost(int serverProcessId)
    {
        return serverProcessId == Environment.ProcessId &&
            string.Equals(_pipeName, BuildPipeName(_processId), StringComparison.Ordinal);
    }

    private static int? TryGetConnectedServerProcessId(NamedPipeClientStream pipe)
    {
        try
        {
            return GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverProcessId)
                ? checked((int)serverProcessId)
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint serverProcessId);

    /// <summary>
    /// Send request and wait for response.
    /// Uses a SemaphoreSlim to serialize pipe access across async calls.
    /// </summary>
    /// <param name="method">Inspector method name to call</param>
    /// <param name="requestId">Unique request identifier</param>
    /// <param name="requestParams">Parameters to send with the request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<InspectorResponse> SendRequestAsync(
        string method,
        string requestId,
        object? requestParams,
        CancellationToken cancellationToken)
    {
        using var requestTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeoutCts.CancelAfter(_requestTimeout);

        var lockAcquired = false;
        try
        {
            await _pipeSemaphore.WaitAsync(requestTimeoutCts.Token).ConfigureAwait(false);
            lockAcquired = true;

            return await SendRequestCoreAsync(method, requestId, requestParams, requestTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (requestTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (lockAcquired)
            {
                ResetConnectionState();
            }

            throw CreateRequestTimeoutException(ex);
        }
        finally
        {
            if (lockAcquired)
            {
                _pipeSemaphore.Release();
            }
        }
    }

    private TimeoutException CreateRequestTimeoutException(Exception? innerException = null)
    {
        return new TimeoutException(
            $"Timed out waiting {Math.Ceiling(_requestTimeout.TotalMilliseconds)}ms for an Inspector response.",
            innerException);
    }

    private async Task<InspectorResponse> SendRequestCoreAsync(
        string method,
        string requestId,
        object? requestParams,
        CancellationToken cancellationToken)
    {
        var communicationStarted = false;
        try
        {
            // Re-check disposed state after acquiring semaphore
            Stream commStream;
            lock (_lock)
            {
                if (Volatile.Read(ref _disposeState) != 0)
                {
                    throw new ObjectDisposedException(nameof(NamedPipeClient), "Client has been disposed");
                }

                if (_pipeClient == null || !_pipeClient.IsConnected || _communicationStream == null)
                {
                    throw new InvalidOperationException("Client is not connected");
                }
                commStream = _communicationStream;
            }

            var correlationId = Guid.NewGuid().ToString();

            // Create request
            var request = new InspectorRequest
            {
                Id = requestId,
                Method = method,
                Params = requestParams != null ? JsonSerializer.SerializeToElement(requestParams) : null,
                CorrelationId = correlationId
            };

            // Serialize and send
            communicationStarted = true;
            var requestJson = JsonSerializer.Serialize(request, IpcSerializerOptions);
            await MessageFraming.WriteMessageAsync(commStream, requestJson, cancellationToken).ConfigureAwait(false);

            // Read response
            var responseJson = await MessageFraming.ReadMessageAsync(commStream, cancellationToken).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson, IpcSerializerOptions)
                ?? throw new InvalidOperationException("Invalid response from Inspector");

            if (!string.Equals(response.Id, requestId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected response id '{response.Id}' for request '{requestId}'.");
            }

            if (!string.IsNullOrEmpty(response.CorrelationId) &&
                !string.Equals(response.CorrelationId, correlationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected response correlation id '{response.CorrelationId}' for request '{correlationId}'.");
            }

            return response;
        }
        catch (OperationCanceledException) when (communicationStarted)
        {
            ResetConnectionState();
            throw;
        }
        catch (IOException) when (communicationStarted)
        {
            ResetConnectionState();
            throw;
        }
        catch (JsonException ex) when (communicationStarted)
        {
            ResetConnectionState();
            throw new InvalidOperationException("Invalid response from Inspector", ex);
        }
        catch (InvalidOperationException) when (communicationStarted)
        {
            ResetConnectionState();
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            if (communicationStarted)
            {
                ResetConnectionState();
            }

            throw new InvalidOperationException("Pipe connection was closed during communication", ex);
        }
    }

    /// <summary>
    /// Dispose the Named Pipe client and release resources
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        if (_pipeSemaphore.Wait(DisposeSemaphoreWaitTimeout))
        {
            try
            {
                ResetConnectionState();
            }
            finally
            {
                _pipeSemaphore.Release();
            }
        }
        else
        {
            ResetConnectionState();
        }

        // NOTE: _pipeSemaphore is intentionally NOT disposed here.
        // A concurrent SendRequestAsync may still be holding/awaiting the semaphore.
        // Disposing it while awaited would throw ObjectDisposedException.
        // SemaphoreSlim is lightweight and will be collected by GC.
    }
}
