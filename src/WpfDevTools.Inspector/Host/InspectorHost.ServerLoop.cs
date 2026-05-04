using System.IO;
using System.IO.Pipes;
using System.Net.Security;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Host;

public sealed partial class InspectorHost
{
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
}
