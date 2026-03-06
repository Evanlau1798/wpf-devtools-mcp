using System.IO;
using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Hosts the Named Pipe server for Inspector communication
/// </summary>
public sealed partial class InspectorHost : IDisposable
{
    private readonly int _processId;
    private readonly string _pipeName;
    private readonly FileLogger _logger;
    private readonly RequestDispatcher _dispatcher;
    private readonly AuthenticationManager? _authManager;
    private readonly CertificateManager? _certManager;
    private readonly ChallengeGenerator _challengeGenerator;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    private volatile bool _isRunning;
    private readonly object _lock = new object();

    /// <summary>
    /// Create a new InspectorHost instance without authentication or encryption (backward compatible)
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    public InspectorHost(int processId)
        : this(processId, CreatePipeName(processId), null, null)
    {
    }

    internal InspectorHost(int processId, string pipeName)
        : this(processId, pipeName, null, null)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    public InspectorHost(int processId, AuthenticationManager? authManager)
        : this(processId, CreatePipeName(processId), authManager, null)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication and encryption
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for SslStream encryption (null to disable encryption)</param>
    public InspectorHost(int processId, AuthenticationManager? authManager, CertificateManager? certManager)
        : this(processId, CreatePipeName(processId), authManager, certManager)
    {
    }

    internal InspectorHost(
        int processId,
        string pipeName,
        AuthenticationManager? authManager,
        CertificateManager? certManager)
    {
        _processId = processId;
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? CreatePipeName(processId)
            : pipeName;
        var logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_Inspector_{processId}.log");
        _logger = new FileLogger(logPath);
        _dispatcher = new RequestDispatcher(_logger);
        _authManager = authManager;
        _certManager = certManager;
        _challengeGenerator = new ChallengeGenerator();
    }

    private static string CreatePipeName(int processId) => $"WpfDevTools_{processId}";

    /// <summary>
    /// Start the Named Pipe server
    /// </summary>
    /// <remarks>
    /// This method is idempotent - calling it multiple times has no effect if server is already running.
    /// </remarks>
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
    /// <remarks>
    /// This method is idempotent - calling it multiple times has no effect if server is already stopped.
    /// Waits up to ShutdownTimeout for server task to complete, then disposes resources.
    /// </remarks>
    public void Stop()
    {
        Task? taskToWait = null;
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _cancellationTokenSource?.Cancel();
            _isRunning = false;
            taskToWait = _serverTask;
        }

        // CRITICAL FIX: Check Wait() return value and log timeout
        if (taskToWait != null)
        {
            bool completed = taskToWait.Wait(InspectorConfig.ShutdownTimeout);
            if (!completed)
            {
                LogError($"Server task did not complete within {InspectorConfig.ShutdownTimeout.TotalMilliseconds}ms timeout");
            }
        }

        // Dispose pipe server and CTS after task completes
        lock (_lock)
        {
            _pipeServer?.Dispose();
            _pipeServer = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        // Clean up analyzer resources
        try
        {
            PerformanceAnalyzer.ResetMonitoring();
        }
        catch (Exception ex)
        {
            LogError($"Failed to reset performance monitoring: {ex.Message}");
        }

        try
        {
            DependencyPropertyAnalyzer.StopAllWatchers();
        }
        catch (Exception ex)
        {
            LogError($"Failed to stop DP watchers: {ex.Message}");
        }

        try
        {
            BindingErrorTraceListener.Uninstall();
        }
        catch (Exception ex)
        {
            LogError($"Failed to uninstall BindingErrorTraceListener: {ex.Message}");
        }
    }

    private async Task RunServerLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Create new pipe server instance with ACL restricted to current user
                _pipeServer = CreateSecurePipeServer();

                // Wait for client connection
                await _pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                // Authenticate client if authentication is enabled
                if (_authManager != null && _authManager.IsAuthenticationEnabled)
                {
                    if (!await AuthenticateClientAsync(_pipeServer, cancellationToken).ConfigureAwait(false))
                    {
                        LogError("Authentication failed: client provided invalid response");
                        continue; // finally block will dispose pipe, loop creates new one
                    }
                }

                // Establish encrypted stream if certificate manager is provided
                Stream communicationStream = _pipeServer;
                SslStream? sslStream = null;

                if (_certManager != null)
                {
                    sslStream = await CreateServerSslStreamAsync(_pipeServer, cancellationToken).ConfigureAwait(false);
                    if (sslStream == null)
                    {
                        LogError("TLS handshake failed");
                        continue;
                    }
                    communicationStream = sslStream;
                }

                try
                {
                    // Handle client requests over (possibly encrypted) stream
                    await HandleClientAsync(communicationStream, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    sslStream?.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (IOException ex)
            {
                LogError($"Pipe I/O error: {ex.Message}");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError($"Pipe access denied: {ex.Message}");
                break; // Don't retry on access denied
            }
            finally
            {
                lock (_lock)
                {
                    _pipeServer?.Dispose();
                    _pipeServer = null;
                }
            }
        }
    }

    private async Task HandleClientAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Read request
                var requestJson = await MessageFraming.ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);

                // Parse request
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                if (request == null)
                {
                    await SendErrorResponseAsync(stream, "unknown", "Invalid request format", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var stopwatch = Stopwatch.StartNew();
                // Process request
                var response = await ProcessRequestAsync(request, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                _logger.LogRequest(
                    request.Method,
                    request.CorrelationId,
                    _processId,
                    stopwatch.ElapsedMilliseconds,
                    response.Error == null,
                    response.Error?.Message);

                // Send response
                var responseJson = JsonSerializer.Serialize(response);
                await MessageFraming.WriteMessageAsync(stream, responseJson, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Client disconnected
        }
        catch (ObjectDisposedException)
        {
            // Pipe was disposed during shutdown - normal cleanup path
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
        timeoutCts.CancelAfter(InspectorConfig.RequestTimeout);

        try
        {
            return await _dispatcher.DispatchAsync(request, timeoutCts.Token).ConfigureAwait(false);
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }

    private async Task SendErrorResponseAsync(
        Stream stream,
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
            await MessageFraming.WriteMessageAsync(stream, responseJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send error response: {ex.Message}");
        }
    }

    private void LogError(string message)
    {
        _logger.LogError(message);
    }

    /// <summary>
    /// Gets whether the Inspector server is currently running
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Dispose resources and stop the Inspector server
    /// </summary>
    public void Dispose()
    {
        Stop(); // Stop() disposes _pipeServer and _cancellationTokenSource
        _dispatcher.Dispose();
        _logger.Dispose();
    }
}







