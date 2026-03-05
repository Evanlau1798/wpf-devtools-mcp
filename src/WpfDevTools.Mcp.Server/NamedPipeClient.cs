using System.IO.Pipes;
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
    private NamedPipeClientStream? _pipeClient;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _pipeSemaphore = new(1, 1);
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the NamedPipeClient class without authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    public NamedPipeClient(int processId)
        : this(processId, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the NamedPipeClient class with optional authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    public NamedPipeClient(int processId, AuthenticationManager? authManager)
    {
        _processId = processId;
        _pipeName = $"WpfDevTools_{processId}";
        _authManager = authManager;
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

                return true;
            }
            catch (Exception)
            {
                if (attempt == maxRetries)
                    return false;

                await Task.Delay(500).ConfigureAwait(false); // Wait before retry
            }
        }

        return false;
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
    public async Task<InspectorResponse> SendRequestAsync<T>(
        string requestId,
        T requestParams,
        CancellationToken cancellationToken)
    {
        await _pipeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // CRITICAL FIX: Re-check disposed state after acquiring semaphore
            NamedPipeClientStream pipeClient;
            lock (_lock)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(NamedPipeClient), "Client has been disposed");
                }

                if (!IsConnected || _pipeClient == null)
                {
                    throw new InvalidOperationException("Client is not connected");
                }
                pipeClient = _pipeClient;
            }

            // CRITICAL FIX: Generate correlation ID for request tracing
            var correlationId = Guid.NewGuid().ToString();

            // Create request
            var request = new InspectorRequest
            {
                Id = requestId,
                Method = GetMethodName(requestParams),
                Params = requestParams != null ? JsonSerializer.SerializeToElement(requestParams) : null,
                CorrelationId = correlationId
            };

            // Serialize and send
            var requestJson = JsonSerializer.Serialize(request);
            await MessageFraming.WriteMessageAsync(pipeClient, requestJson, cancellationToken).ConfigureAwait(false);

            // Read response
            var responseJson = await MessageFraming.ReadMessageAsync(pipeClient, cancellationToken).ConfigureAwait(false);
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

    private string GetMethodName<T>(T requestParams)
    {
        // Extract method name from request params
        // For now, use a simple convention
        var type = requestParams?.GetType();
        return type?.GetProperty("method")?.GetValue(requestParams)?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Dispose the Named Pipe client and release resources
    /// </summary>
    public void Dispose()
    {
        NamedPipeClientStream? pipeToDispose = null;

        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            pipeToDispose = _pipeClient;
            _pipeClient = null;
        }

        // CRITICAL FIX: Dispose pipe and semaphore outside lock
        // Wait briefly to allow in-flight operations to complete
        pipeToDispose?.Dispose();
        System.Threading.Thread.Sleep(100);
        _pipeSemaphore?.Dispose();
    }
}
