using System.IO;
using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
    private const int LifecycleStopped = 0;
    private const int LifecycleStarting = 1;
    private const int LifecycleRunning = 2;
    private const int LifecycleStopping = 3;

    private readonly int _processId;
    private readonly string _pipeName;
    private readonly FileLogger _logger;
    private readonly RequestDispatcher _dispatcher;
    private readonly AuthenticationManager? _authManager;
    private readonly CertificateManager? _certManager;
    private readonly ChallengeGenerator _challengeGenerator;
    private readonly Func<NamedPipeServerStream>? _pipeServerFactory;
    private readonly Action? _beforeStartupCompletion;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _sessionReadTimeout;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private TaskCompletionSource<object?>? _startupCompletionSource;
    private Task? _serverTask;
    private Task? _stopCompletionTask;
    private volatile bool _isRunning;
    private int _lifecycleState;
    private long _nextServerGeneration;
    private long _activeServerGeneration;
    private long _nextStopOperationId;
    private int _disposeState;
    private readonly object _lock = new object();

    internal static Action ResetMonitoringAction { get; set; } = static () => PerformanceAnalyzer.ResetMonitoring();
    internal static Action StopAllWatchersAction { get; set; } = static () => DependencyPropertyAnalyzer.StopAllWatchers();
    internal static Action UninstallBindingTraceListenerAction { get; set; } = static () => BindingErrorTraceListener.Uninstall();

    /// <summary>
    /// Create a new InspectorHost instance without authentication or encryption (backward compatible)
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    public InspectorHost(int processId)
        : this(processId, CreatePipeName(processId), null, null, FileLogLevel.Warning)
    {
    }

    internal InspectorHost(int processId, string pipeName)
        : this(processId, pipeName, null, null, FileLogLevel.Warning)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with an explicit minimum log level.
    /// </summary>
    public InspectorHost(int processId, FileLogLevel minimumLogLevel)
        : this(processId, CreatePipeName(processId), null, null, minimumLogLevel)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">
    /// Authentication manager (null to disable authentication).
    /// Ownership is transferred to the host and the manager is disposed when the host is disposed.
    /// </param>
    public InspectorHost(int processId, AuthenticationManager? authManager)
        : this(processId, CreatePipeName(processId), authManager, null, FileLogLevel.Warning)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication and encryption
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">
    /// Authentication manager (null to disable authentication).
    /// Ownership is transferred to the host and the manager is disposed when the host is disposed.
    /// </param>
    /// <param name="certManager">Certificate manager for SslStream encryption (null to disable encryption)</param>
    public InspectorHost(int processId, AuthenticationManager? authManager, CertificateManager? certManager)
        : this(processId, CreatePipeName(processId), authManager, certManager, FileLogLevel.Warning)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication, encryption, and explicit log level.
    /// </summary>
    public InspectorHost(int processId, AuthenticationManager? authManager, CertificateManager? certManager, FileLogLevel minimumLogLevel)
        : this(processId, CreatePipeName(processId), authManager, certManager, minimumLogLevel)
    {
    }

    internal InspectorHost(
        int processId,
        string pipeName,
        AuthenticationManager? authManager,
        CertificateManager? certManager,
        FileLogLevel minimumLogLevel = FileLogLevel.Warning,
        Func<NamedPipeServerStream>? pipeServerFactory = null,
        TimeSpan? startupTimeout = null,
        Action? beforeStartupCompletion = null,
        TimeSpan? sessionReadTimeout = null)
    {
        _processId = processId;
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? CreatePipeName(processId)
            : pipeName;
        var logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_Inspector_{processId}.log");
        _logger = new FileLogger(logPath)
        {
            MinimumLevel = minimumLogLevel
        };
        _dispatcher = new RequestDispatcher(_logger, _processId, null);
        _authManager = authManager;
        _certManager = certManager;
        _challengeGenerator = new ChallengeGenerator();
        _pipeServerFactory = pipeServerFactory;
        _startupTimeout = startupTimeout ?? InspectorConfig.PipeConnectTimeout;
        _beforeStartupCompletion = beforeStartupCompletion;
        _sessionReadTimeout = sessionReadTimeout ?? InspectorConfig.IdleConnectionTimeout;
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
        while (true)
        {
            Task startupTask;
            Task startupReadinessTask = Task.CompletedTask;
            Task? serverTask = null;
            Task? stopTask = null;
            CancellationTokenSource? cancellationTokenSource = null;
            TaskCompletionSource<object?>? startupCompletionSource = null;
            TaskCompletionSource<object?>? startupReadinessCompletionSource = null;
            long generation;
            var ownsStartup = false;

            lock (_lock)
            {
                if (_disposeState != 0)
                {
                    throw new ObjectDisposedException(nameof(InspectorHost));
                }

                if (_stopCompletionTask != null)
                {
                    stopTask = _stopCompletionTask;
                    generation = _activeServerGeneration;
                    startupTask = Task.CompletedTask;
                }
                else
                {
                    if (_lifecycleState == LifecycleRunning)
                    {
                        return;
                    }

                    if (_lifecycleState == LifecycleStarting)
                    {
                        generation = _activeServerGeneration;
                        startupTask = _startupCompletionSource!.Task;
                    }
                    else if (_lifecycleState == LifecycleStopping)
                    {
                        stopTask = _serverTask;
                        generation = _activeServerGeneration;
                        startupTask = Task.CompletedTask;
                    }
                    else
                    {
                        ownsStartup = true;
                        _lifecycleState = LifecycleStarting;
                        generation = ++_nextServerGeneration;
                        _activeServerGeneration = generation;
                        _cancellationTokenSource = new CancellationTokenSource();
                        cancellationTokenSource = _cancellationTokenSource;
                        _startupCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        startupCompletionSource = _startupCompletionSource;
                        startupTask = _startupCompletionSource.Task;
                        startupReadinessCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        startupReadinessTask = startupReadinessCompletionSource.Task;
                        var startupCancellationToken = _cancellationTokenSource.Token;
                        _serverTask = Task.Factory
                            .StartNew(
                                () => RunServerLoop(startupCancellationToken, startupReadinessCompletionSource, generation),
                                CancellationToken.None,
                                TaskCreationOptions.LongRunning,
                                TaskScheduler.Default)
                            .Unwrap();
                        serverTask = _serverTask;
                    }
                }
            }

            if (stopTask != null)
            {
                WaitForStopCompletion(stopTask);
                continue;
            }

            if (ownsStartup)
            {
                try
                {
                    WaitForStartup(startupReadinessTask, serverTask!);
                    _beforeStartupCompletion?.Invoke();
                    CompleteStartupSuccess(generation, startupTask);
                    EnsureStartupReachedRunning(generation);
                }
                catch (Exception ex)
                {
                    CompleteStartupFailure(generation, cancellationTokenSource!, startupCompletionSource!, serverTask!, ex);
                    ExceptionDispatchInfo.Capture(ex).Throw();
                }

                return;
            }

            startupTask.GetAwaiter().GetResult();
            CompleteStartupSuccess(generation, startupTask);
            EnsureStartupReachedRunning(generation);
            return;
        }
    }


    /// <summary>
    /// Stop the Named Pipe server
    /// </summary>
    /// <remarks>
    /// This method is idempotent - calling it multiple times has no effect if server is already stopped.
    /// Cancellation and pipe teardown happen inline; final server-task waiting and analyzer cleanup complete in the background.
    /// </remarks>
    public void Stop()
    {
        Task? serverTask;
        CancellationTokenSource? cancellationTokenSource;
        long stopOperationId;

        lock (_lock)
        {
            if (_lifecycleState == LifecycleStopped || _lifecycleState == LifecycleStopping)
            {
                return;
            }

            _lifecycleState = LifecycleStopping;
            _activeServerGeneration = 0;
            _cancellationTokenSource?.Cancel();
            _isRunning = false;
            _pipeServer?.Dispose();
            _pipeServer = null;

            serverTask = _serverTask;
            _serverTask = null;
            cancellationTokenSource = _cancellationTokenSource;
            _cancellationTokenSource = null;
            _lifecycleState = LifecycleStopped;

            stopOperationId = ++_nextStopOperationId;
            _stopCompletionTask = Task.Run(() => CompleteStop(serverTask, cancellationTokenSource, stopOperationId));
        }
    }

    private void CompleteStop(Task? serverTask, CancellationTokenSource? cancellationTokenSource, long stopOperationId)
    {
        if (!WaitForServerTaskShutdown(serverTask))
        {
            var deferredCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                if (_nextStopOperationId == stopOperationId)
                {
                    _stopCompletionTask = deferredCompletionSource.Task;
                }
            }

            serverTask!.ContinueWith(
                completedTask =>
                {
                    try
                    {
                        if (completedTask.IsFaulted && completedTask.Exception is { } exception)
                        {
                            LogError($"Server task failed after shutdown timeout: {exception.Flatten().Message}");
                        }

                        CompleteStopFinalization(cancellationTokenSource, stopOperationId);
                        deferredCompletionSource.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        deferredCompletionSource.TrySetException(ex);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return;
        }

        CompleteStopFinalization(cancellationTokenSource, stopOperationId);
    }

    private void CompleteStopFinalization(CancellationTokenSource? cancellationTokenSource, long stopOperationId)
    {
        RunPostStopCleanup();
        cancellationTokenSource?.Dispose();

        lock (_lock)
        {
            if (_nextStopOperationId == stopOperationId)
            {
                _stopCompletionTask = null;
            }
        }
    }

    private bool WaitForServerTaskShutdown(Task? serverTask)
    {
        if (serverTask == null)
        {
            return true;
        }

        try
        {
            bool completed = serverTask.Wait(InspectorConfig.ShutdownTimeout);
            if (!completed)
            {
                LogError($"Server task did not complete within {InspectorConfig.ShutdownTimeout.TotalMilliseconds}ms timeout");
                return false;
            }
        }
        catch (AggregateException ex) when (
            serverTask.IsCanceled ||
            ex.Flatten().InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
            // Cancellation is the normal shutdown path for the server loop.
        }
        catch (AggregateException ex)
        {
            LogError($"Server task failed during shutdown: {ex.Flatten().Message}");
        }

        return true;
    }

    private void RunPostStopCleanup()
    {
        TryRunCleanupAction("reset performance monitoring", ResetMonitoringAction);
        TryRunCleanupAction("stop DP watchers", StopAllWatchersAction);
        TryRunCleanupAction("uninstall BindingErrorTraceListener", UninstallBindingTraceListenerAction);
    }

    private void TryRunCleanupAction(string operationName, Action cleanupAction)
    {
        try
        {
            cleanupAction();
        }
        catch (Exception ex)
        {
            LogError($"Failed to {operationName}: {ex.Message}");
        }
    }

    private async Task RunServerLoop(
        CancellationToken cancellationToken,
        TaskCompletionSource<object?> startupSignal,
        long generation)
    {
        while (!cancellationToken.IsCancellationRequested && IsActiveServerGeneration(generation))
        {
            NamedPipeServerStream? pipeServer = null;

            try
            {
                // Create new pipe server instance with ACL restricted to current user
                pipeServer = CreateSecurePipeServer();
                if (!TryPublishPipeServer(generation, pipeServer))
                {
                    break;
                }

                startupSignal.TrySetResult(null);

                // Wait for client connection
                await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await global::WpfDevTools.Inspector.IntegrationTestDelayHooks
                    .DelayAfterPipeConnectIfConfiguredAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Authenticate client if authentication is enabled
                if (_authManager != null && _authManager.IsAuthenticationEnabled)
                {
                    if (!await AuthenticateClientAsync(pipeServer, cancellationToken).ConfigureAwait(false))
                    {
                        LogError("Authentication failed: client provided invalid response");
                        continue; // finally block will dispose pipe, loop creates new one
                    }
                }

                // Establish encrypted stream if certificate manager is provided
                Stream communicationStream = pipeServer;
                SslStream? sslStream = null;

                if (_certManager != null)
                {
                    sslStream = await CreateServerSslStreamAsync(pipeServer, cancellationToken).ConfigureAwait(false);
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
                startupSignal.TrySetCanceled();
                // Normal shutdown
                break;
            }
            catch (IOException ex)
            {
                if (!startupSignal.Task.IsCompleted)
                {
                    startupSignal.TrySetException(ex);
                    break;
                }

                LogError($"Pipe I/O error: {ex.Message}");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!startupSignal.Task.IsCompleted)
                {
                    startupSignal.TrySetException(ex);
                    break;
                }

                LogError($"Pipe access denied (attempt will retry): {ex.Message}");
                // Pipe name may still be registered in kernel after rapid disconnect/reconnect.
                // Retry with delay instead of breaking permanently.
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!startupSignal.Task.IsCompleted)
                {
                    startupSignal.TrySetException(ex);
                }
                else
                {
                    LogError($"Unhandled server loop error: {ex.Message}");
                }

                break;
            }
            finally
            {
                ClearPublishedPipeServer(pipeServer);
                pipeServer?.Dispose();
            }
        }

        lock (_lock)
        {
            if (_activeServerGeneration == generation && _lifecycleState != LifecycleStopping)
            {
                _activeServerGeneration = 0;
                _serverTask = null;
                _lifecycleState = LifecycleStopped;
                _isRunning = false;
            }
        }
    }

    private void CompleteStartupSuccess(long generation, Task startupTask)
    {
        lock (_lock)
        {
            var startupCompletionSource = _startupCompletionSource;
            if (_lifecycleState == LifecycleStarting &&
                _activeServerGeneration == generation &&
                ReferenceEquals(startupCompletionSource?.Task, startupTask))
            {
                startupCompletionSource.TrySetResult(null);
                _startupCompletionSource = null;
                _lifecycleState = LifecycleRunning;
                _isRunning = true;
            }
        }
    }

    private void EnsureStartupReachedRunning(long generation)
    {
        lock (_lock)
        {
            if (_lifecycleState == LifecycleRunning &&
                _activeServerGeneration == generation &&
                _isRunning)
            {
                return;
            }
        }

        throw new OperationCanceledException("InspectorHost startup was canceled before reaching the running state.");
    }

    private void CompleteStartupFailure(
        long generation,
        CancellationTokenSource cancellationTokenSource,
        TaskCompletionSource<object?> startupCompletionSource,
        Task serverTask,
        Exception startupError)
    {
        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Stop() may have already disposed the startup CTS after timing out.
        }

        lock (_lock)
        {
            if (_activeServerGeneration == generation)
            {
                _activeServerGeneration = 0;
                _pipeServer?.Dispose();
                _pipeServer = null;
                _isRunning = false;
            }
        }

        try
        {
            bool completed = serverTask.Wait(InspectorConfig.ShutdownTimeout);
            if (!completed)
            {
                LogError($"Server task did not complete within {InspectorConfig.ShutdownTimeout.TotalMilliseconds}ms timeout during startup cleanup");
            }
        }
        catch (AggregateException ex) when (
            serverTask.IsCanceled ||
            ex.Flatten().InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
            // Cancellation is the normal cleanup path when startup fails.
        }
        catch (AggregateException ex)
        {
            LogError($"Server task failed during startup cleanup: {ex.Flatten().Message}");
        }

        lock (_lock)
        {
            if (ReferenceEquals(_cancellationTokenSource, cancellationTokenSource))
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }

            if (ReferenceEquals(_serverTask, serverTask))
            {
                _serverTask = null;
            }

            if (ReferenceEquals(_startupCompletionSource, startupCompletionSource))
            {
                _startupCompletionSource = null;
            }

            if ((_activeServerGeneration == 0 || _activeServerGeneration == generation) &&
                _serverTask == null &&
                _startupCompletionSource == null)
            {
                _lifecycleState = LifecycleStopped;
                _isRunning = false;
            }
        }

        startupCompletionSource.TrySetException(startupError);

        startupCompletionSource?.TrySetException(startupError);
    }

    private void WaitForStopCompletion(Task stopTask)
    {
        try
        {
            bool completed = stopTask.Wait(InspectorConfig.ShutdownTimeout);
            if (!completed)
            {
                throw new TimeoutException(
                    $"Timed out after {InspectorConfig.ShutdownTimeout.TotalMilliseconds}ms while waiting for InspectorHost startup cleanup.");
            }
        }
        catch (AggregateException ex) when (
            stopTask.IsCanceled ||
            ex.Flatten().InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
            // Stop or startup-failure cleanup completed through cancellation.
        }
    }

    private bool IsActiveServerGeneration(long generation)
    {
        lock (_lock)
        {
            return _activeServerGeneration == generation;
        }
    }

    private bool TryPublishPipeServer(long generation, NamedPipeServerStream pipeServer)
    {
        lock (_lock)
        {
            if (_activeServerGeneration != generation ||
                (_lifecycleState != LifecycleStarting && _lifecycleState != LifecycleRunning))
            {
                return false;
            }

            _pipeServer = pipeServer;
            return true;
        }
    }

    private void ClearPublishedPipeServer(NamedPipeServerStream? pipeServer)
    {
        if (pipeServer == null)
        {
            return;
        }

        lock (_lock)
        {
            if (ReferenceEquals(_pipeServer, pipeServer))
            {
                _pipeServer = null;
            }
        }
    }

    private void WaitForStartup(Task startupTask, Task serverTask)
    {
        var completedTask = Task.WhenAny(
            startupTask,
            serverTask,
            Task.Delay(_startupTimeout)).GetAwaiter().GetResult();

        if (ReferenceEquals(completedTask, startupTask))
        {
            startupTask.GetAwaiter().GetResult();
            return;
        }

        if (ReferenceEquals(completedTask, serverTask))
        {
            if (startupTask.IsCompleted)
            {
                startupTask.GetAwaiter().GetResult();
            }

            serverTask.GetAwaiter().GetResult();
            throw new InvalidOperationException("InspectorHost server loop exited before startup completed.");
        }

        throw new TimeoutException(
            $"Timed out after {_startupTimeout.TotalMilliseconds}ms while starting InspectorHost pipe '{_pipeName}'.");
    }

    private static readonly JsonSerializerOptions IpcSerializerOptions = new()
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = true
    };

    private async Task HandleClientAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string requestJson;
                try
                {
                    requestJson = await ReadMessageWithSessionTimeoutAsync(stream, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (TimeoutException ex)
                {
                    LogError(ex.Message);
                    break;
                }

                // Parse request with shared options and validate the IPC contract shape.
                var parseResult = DeserializeRequest(requestJson);
                var request = parseResult.Request;
                if (request == null)
                {
                    await SendErrorResponseAsync(
                        stream,
                        parseResult.RequestId,
                        parseResult.ErrorMessage,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var requestLoggingEnabled = _logger.IsEnabled(FileLogLevel.Info);
                Stopwatch? requestLogStopwatch = requestLoggingEnabled
                    ? Stopwatch.StartNew()
                    : null;

                // Process request
                var response = await ProcessRequestAsync(request, cancellationToken).ConfigureAwait(false);

                if (requestLogStopwatch != null)
                {
                    requestLogStopwatch.Stop();
                    _logger.LogRequest(
                        request.Method,
                        request.CorrelationId,
                        _processId,
                        requestLogStopwatch.ElapsedMilliseconds,
                        response.Error == null,
                        response.Error?.Message);
                }

                // Send response
                var responseJson = JsonSerializer.Serialize(response, IpcSerializerOptions);
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

    private async Task<string> ReadMessageWithSessionTimeoutAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var readTask = MessageFraming.ReadMessageAsync(stream, cancellationToken);
        var timeoutTask = Task.Delay(_sessionReadTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);

        if (ReferenceEquals(completedTask, readTask))
        {
            return await readTask.ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        ObserveTimedOutReadTask(readTask);

        try
        {
            stream.Dispose();
        }
        catch (Exception ex)
        {
            LogError($"Failed to dispose timed-out client stream: {ex.Message}");
        }

        throw new TimeoutException($"Client session read timed out after {_sessionReadTimeout.TotalMilliseconds}ms");
    }

    private static void ObserveTimedOutReadTask(Task<string> readTask)
    {
        _ = readTask.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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

    internal bool IsDisposed => System.Threading.Volatile.Read(ref _disposeState) == 2;

    internal bool OwnsAuthenticationManager(AuthenticationManager? authenticationManager)
    {
        return ReferenceEquals(_authManager, authenticationManager);
    }

    /// <summary>
    /// Dispose resources and stop the Inspector server
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        Exception? cleanupError = null;

        try
        {
            Stop(); // Stop() disposes _pipeServer and _cancellationTokenSource
        }
        catch (Exception ex)
        {
            cleanupError = ex;
        }

        try
        {
            WaitForStopFinalization();
        }
        catch (Exception ex)
        {
            cleanupError = cleanupError == null ? ex : new AggregateException(cleanupError, ex);
        }

        try
        {
            _dispatcher.Dispose();
        }
        catch (Exception ex)
        {
            cleanupError = cleanupError == null ? ex : new AggregateException(cleanupError, ex);
        }

        try
        {
            _logger.Dispose();
        }
        catch (Exception ex)
        {
            cleanupError = cleanupError == null ? ex : new AggregateException(cleanupError, ex);
        }

        try
        {
            _authManager?.Dispose();
        }
        catch (Exception ex)
        {
            cleanupError = cleanupError == null ? ex : new AggregateException(cleanupError, ex);
        }

        Interlocked.Exchange(ref _disposeState, cleanupError == null ? 2 : 3);

        if (cleanupError != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupError).Throw();
        }
    }

    private void WaitForStopFinalization()
    {
        Task? stopCompletionTask;

        lock (_lock)
        {
            stopCompletionTask = _stopCompletionTask;
        }

        if (stopCompletionTask == null)
        {
            return;
        }

        try
        {
            bool completed = stopCompletionTask.Wait(InspectorConfig.ShutdownTimeout);
            if (!completed)
            {
                LogError($"Stop finalization did not complete within {InspectorConfig.ShutdownTimeout.TotalMilliseconds}ms timeout");
            }
        }
        catch (AggregateException ex) when (
            stopCompletionTask.IsCanceled ||
            ex.Flatten().InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
            // Cancellation is the normal finalization path when disposal races with shutdown.
        }
        catch (AggregateException ex)
        {
            LogError($"Stop finalization failed: {ex.Flatten().Message}");
        }
    }
}







