using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Named Pipe client for communicating with Inspector DLL
/// </summary>
public class NamedPipeClient : IDisposable
{
    private readonly int _processId;
    private readonly string _pipeName;
    private readonly AuthenticationManager? _authManager;
    private readonly CertificateManager? _certManager;
    private NamedPipeClientStream? _pipeClient;
    private Stream? _communicationStream;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _pipeSemaphore = new(1, 1);
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the NamedPipeClient class without authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    public NamedPipeClient(int processId)
        : this(processId, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the NamedPipeClient class with optional authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    public NamedPipeClient(int processId, AuthenticationManager? authManager)
        : this(processId, authManager, null)
    {
    }

    /// <summary>
    /// Initializes a new instance with optional authentication and encryption
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for SslStream encryption (null to disable encryption)</param>
    public NamedPipeClient(int processId, AuthenticationManager? authManager, CertificateManager? certManager)
    {
        _processId = processId;
        _pipeName = $"WpfDevTools_{processId}";
        _authManager = authManager;
        _certManager = certManager;
    }

    /// <summary>
    /// Pipe name
    /// </summary>
    public string PipeName => _pipeName;

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
    public async Task<bool> ConnectAsync(TimeSpan timeout, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                NamedPipeClientStream localClient;
                lock (_lock)
                {
                    if (_isDisposed)
                        return false;

                    _pipeClient?.Dispose();
                    _pipeClient = new NamedPipeClientStream(
                        ".",
                        _pipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous);
                    localClient = _pipeClient;
                }

                using var cts = new CancellationTokenSource(timeout);
                await localClient.ConnectAsync(cts.Token).ConfigureAwait(false);

                // Perform authentication if enabled
                if (_authManager != null && _authManager.IsAuthenticationEnabled)
                {
                    if (!await AuthenticateToInspectorAsync(localClient, cts.Token).ConfigureAwait(false))
                    {
                        return false;
                    }
                }

                // Establish encrypted stream if certificate manager is provided
                if (_certManager != null)
                {
                    var sslStream = await CreateClientSslStreamAsync(localClient, cts.Token).ConfigureAwait(false);
                    if (sslStream == null)
                        return false;

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

                return true;
            }
            catch (IOException)
            {
                if (attempt == maxRetries)
                    return false;

                await Task.Delay(500).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (attempt == maxRetries)
                    return false;

                await Task.Delay(500).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private async Task<SslStream?> CreateClientSslStreamAsync(
        NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            var sslStream = new SslStream(pipe, leaveInnerStreamOpen: true,
                (sender, cert, chain, errors) =>
                {
                    // Accept self-signed certificate with correct subject
                    if (cert == null) return false;
                    using var cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);
                    return cert2.Subject == "CN=WpfDevTools-Inspector";
                });

#if NET48
            await sslStream.AuthenticateAsClientAsync("WpfDevTools-Inspector");
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
            return null;
        }
        catch (AuthenticationException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
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
                if (read == 0) return false;
                totalRead += read;
            }

            // 2. Compute HMAC-SHA256 response
            var calculator = new ResponseCalculator(_authManager!.GetSharedSecret());
            var response = calculator.ComputeResponse(challenge);

            // 3. Send response
            await pipe.WriteAsync(response, 0, response.Length, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            // 4. Read 1-byte result from server (1=success, 0=failure)
            var resultBuf = new byte[1];
            var resultRead = await pipe.ReadAsync(resultBuf, 0, 1, cancellationToken).ConfigureAwait(false);
            if (resultRead == 0) return false;

            return resultBuf[0] == 1;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

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
        await _pipeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check disposed state after acquiring semaphore
            Stream commStream;
            lock (_lock)
            {
                if (_isDisposed)
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
            var requestJson = JsonSerializer.Serialize(request);
            await MessageFraming.WriteMessageAsync(commStream, requestJson, cancellationToken).ConfigureAwait(false);

            // Read response
            var responseJson = await MessageFraming.ReadMessageAsync(commStream, cancellationToken).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

            return response ?? throw new InvalidOperationException("Invalid response from Inspector");
        }
        catch (ObjectDisposedException)
        {
            throw new InvalidOperationException("Pipe connection was closed during communication");
        }
        finally
        {
            _pipeSemaphore.Release();
        }
    }

    /// <summary>
    /// Dispose the Named Pipe client and release resources
    /// </summary>
    public void Dispose()
    {
        NamedPipeClientStream? pipeToDispose = null;
        Stream? streamToDispose = null;

        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            streamToDispose = _communicationStream;
            pipeToDispose = _pipeClient;
            _communicationStream = null;
            _pipeClient = null;
        }

        // Dispose SslStream first (if separate from pipe), then pipe
        // SslStream.Dispose may throw if pipe is already broken (e.g., server disconnected)
        if (streamToDispose != null && streamToDispose != pipeToDispose)
        {
            try { streamToDispose.Dispose(); } catch (IOException) { }
        }
        try { pipeToDispose?.Dispose(); } catch (IOException) { }
        _pipeSemaphore?.Dispose();
    }
}
