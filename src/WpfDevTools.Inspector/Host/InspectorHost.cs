using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
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
public class InspectorHost : IDisposable
{
    private readonly int _processId;
    private readonly string _pipeName;
    private readonly string _logPath;
    private readonly RequestDispatcher _dispatcher;
    private readonly AuthenticationManager? _authManager;
    private readonly ChallengeGenerator _challengeGenerator;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    private bool _isRunning;
    private readonly object _lock = new object();

    /// <summary>
    /// Create a new InspectorHost instance without authentication (backward compatible)
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    public InspectorHost(int processId)
        : this(processId, null)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    public InspectorHost(int processId, AuthenticationManager? authManager)
    {
        _processId = processId;
        _pipeName = $"WpfDevTools_{processId}";
        _logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_Inspector_{processId}.log");
        _dispatcher = new RequestDispatcher();
        _authManager = authManager;
        _challengeGenerator = new ChallengeGenerator();
    }

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

        // Dispose pipe server after task completes
        lock (_lock)
        {
            _pipeServer?.Dispose();
            _pipeServer = null;
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

                // Handle client requests
                await HandleClientAsync(_pipeServer, cancellationToken).ConfigureAwait(false);
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

    private NamedPipeServerStream CreateSecurePipeServer()
    {
        var pipeSecurity = new PipeSecurity();

        // Allow current user
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        // Allow SYSTEM account
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            systemSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

#if NET48
        return new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);
#else
        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);
#endif
    }

    private async Task<bool> AuthenticateClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            var token = timeoutCts.Token;

            // 1. Generate and send 32-byte challenge
            var challenge = _challengeGenerator.GenerateChallenge();
            await pipe.WriteAsync(challenge, 0, challenge.Length, token).ConfigureAwait(false);
            await pipe.FlushAsync(token).ConfigureAwait(false);

            // 2. Read 32-byte response from client
            var response = new byte[32];
            var totalRead = 0;
            while (totalRead < 32)
            {
                var read = await pipe.ReadAsync(response, totalRead, 32 - totalRead, token).ConfigureAwait(false);
                if (read == 0)
                {
                    await SendAuthResult(pipe, false, token).ConfigureAwait(false);
                    return false;
                }
                totalRead += read;
            }

            // 3. Verify response using HMAC-SHA256
            var calculator = new ResponseCalculator(_authManager!.GetSharedSecret());
            var isValid = calculator.VerifyResponse(challenge, response);

            // 4. Send 1-byte result to client (1=success, 0=failure)
            await SendAuthResult(pipe, isValid, token).ConfigureAwait(false);

            return isValid;
        }
        catch (OperationCanceledException)
        {
            LogError("Authentication timed out");
            return false;
        }
        catch (IOException ex)
        {
            LogError($"Authentication I/O error: {ex.Message}");
            return false;
        }
    }

    private static async Task SendAuthResult(
        NamedPipeServerStream pipe, bool success, CancellationToken cancellationToken)
    {
        try
        {
            var resultByte = new byte[] { (byte)(success ? 1 : 0) };
            await pipe.WriteAsync(resultByte, 0, 1, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best effort - client may have already disconnected
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
                var requestJson = await MessageFraming.ReadMessageAsync(pipe, cancellationToken).ConfigureAwait(false);

                // Parse request
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                if (request == null)
                {
                    await SendErrorResponseAsync(pipe, "unknown", "Invalid request format", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Process request
                var response = await ProcessRequestAsync(request, cancellationToken).ConfigureAwait(false);

                // Send response
                var responseJson = JsonSerializer.Serialize(response);
                await MessageFraming.WriteMessageAsync(pipe, responseJson, cancellationToken).ConfigureAwait(false);
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
            await MessageFraming.WriteMessageAsync(pipe, responseJson, cancellationToken).ConfigureAwait(false);
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
            File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging errors
        }
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
        Stop(); // Stop() already disposes _pipeServer
        _cancellationTokenSource?.Dispose();
    }
}
