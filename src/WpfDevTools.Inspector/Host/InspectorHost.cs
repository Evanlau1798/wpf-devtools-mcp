using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Hosts the Named Pipe server for Inspector communication
/// </summary>
public class InspectorHost : IDisposable
{
    private readonly int _processId;
    private readonly string _pipeName;
    private readonly RequestDispatcher _dispatcher;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    private bool _isRunning;
    private readonly object _lock = new object();

    public InspectorHost(int processId)
    {
        _processId = processId;
        _pipeName = $"WpfDevTools_{processId}";
        _dispatcher = new RequestDispatcher();
    }

    /// <summary>
    /// Start the Named Pipe server
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _serverTask = Task.Run(() => RunServerLoop(_cancellationTokenSource.Token));
            _isRunning = true;
        }
    }

    /// <summary>
    /// Stop the Named Pipe server
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _cancellationTokenSource?.Cancel();
            _pipeServer?.Dispose();
            _serverTask?.Wait(TimeSpan.FromSeconds(5));
            _isRunning = false;
        }
    }

    private async Task RunServerLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Create new pipe server instance
                _pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                // Wait for client connection
                await _pipeServer.WaitForConnectionAsync(cancellationToken);

                // Handle client requests
                await HandleClientAsync(_pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                LogError($"Server loop error: {ex.Message}");
                await Task.Delay(1000, cancellationToken);
            }
            finally
            {
                _pipeServer?.Dispose();
                _pipeServer = null;
            }
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        try
        {
            while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                // Read request
                var requestJson = await MessageFraming.ReadMessageAsync(pipe, cancellationToken);

                // Parse request
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                if (request == null)
                {
                    await SendErrorResponseAsync(pipe, "unknown", "Invalid request format", cancellationToken);
                    continue;
                }

                // Process request
                var response = await ProcessRequestAsync(request, cancellationToken);

                // Send response
                var responseJson = JsonSerializer.Serialize(response);
                await MessageFraming.WriteMessageAsync(pipe, responseJson, cancellationToken);
            }
        }
        catch (IOException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            LogError($"Client handling error: {ex.Message}");
        }
    }

    private async Task<InspectorResponse> ProcessRequestAsync(
        InspectorRequest request,
        CancellationToken cancellationToken)
    {
        // Delegate to RequestDispatcher with timeout
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            return await _dispatcher.DispatchAsync(request, timeoutCts.Token);
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }

    private async Task SendErrorResponseAsync(
        NamedPipeServerStream pipe,
        string requestId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = new InspectorResponse
            {
                Id = requestId,
                Result = null,
                Error = new InspectorError
                {
                    Code = Shared.Enums.ErrorCode.InvalidRequest,
                    Message = errorMessage,
                    Data = null
                }
            };

            var responseJson = JsonSerializer.Serialize(response);
            await MessageFraming.WriteMessageAsync(pipe, responseJson, cancellationToken);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send error response: {ex.Message}");
        }
    }

    private void LogError(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Path.GetTempPath(),
                $"WpfDevTools_Inspector_{_processId}.log");

            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging errors
        }
    }

    public bool IsRunning => _isRunning;

    public void Dispose()
    {
        Stop();
        _cancellationTokenSource?.Dispose();
        _pipeServer?.Dispose();
    }
}
