using System.IO.Pipes;
using System.Text.Json;
using WpfDevTools.Shared.Messages;
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
    private NamedPipeClientStream? _pipeClient;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _pipeSemaphore = new(1, 1);
    private bool _isDisposed;

    public NamedPipeClient(int processId)
    {
        _processId = processId;
        _pipeName = $"WpfDevTools_{processId}";
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

    /// <summary>
    /// Send request and wait for response.
    /// Uses a SemaphoreSlim to serialize pipe access across async calls.
    /// </summary>
    public async Task<InspectorResponse> SendRequestAsync<T>(
        string requestId,
        T requestParams,
        CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipeClient;
        lock (_lock)
        {
            if (!IsConnected || _pipeClient == null)
            {
                throw new InvalidOperationException("Client is not connected");
            }
            pipeClient = _pipeClient;
        }

        // Create request
        var request = new InspectorRequest
        {
            Id = requestId,
            Method = GetMethodName(requestParams),
            Params = requestParams != null ? JsonSerializer.SerializeToElement(requestParams) : null
        };

        await _pipeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Serialize and send
            var requestJson = JsonSerializer.Serialize(request);
            await MessageFraming.WriteMessageAsync(pipeClient, requestJson, cancellationToken).ConfigureAwait(false);

            // Read response
            var responseJson = await MessageFraming.ReadMessageAsync(pipeClient, cancellationToken).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

            return response ?? throw new InvalidOperationException("Invalid response from Inspector");
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

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _pipeClient?.Dispose();
            _pipeClient = null;
            _pipeSemaphore?.Dispose();
        }
    }
}
