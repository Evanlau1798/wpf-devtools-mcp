using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Host;

public sealed partial class InspectorHost
{
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
            _stopCompletionTask = Task.Run(() => CompleteStopAsync(serverTask, cancellationTokenSource, stopOperationId));
        }
    }

    private async Task CompleteStopAsync(Task? serverTask, CancellationTokenSource? cancellationTokenSource, long stopOperationId)
    {
        if (!await WaitForServerTaskShutdownAsync(serverTask).ConfigureAwait(false))
        {
            var deferredCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                if (_nextStopOperationId == stopOperationId)
                {
                    _stopCompletionTask = deferredCompletionSource.Task;
                }
            }

            _ = CompleteDeferredStopAsync(serverTask!, cancellationTokenSource, stopOperationId, deferredCompletionSource);
            return;
        }

        CompleteStopFinalization(cancellationTokenSource, stopOperationId);
    }

    private async Task CompleteDeferredStopAsync(
        Task serverTask,
        CancellationTokenSource? cancellationTokenSource,
        long stopOperationId,
        TaskCompletionSource<object?> deferredCompletionSource)
    {
        try
        {
            try
            {
                await serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogError($"Server task failed after shutdown timeout: {ex.Message}");
            }

            CompleteStopFinalization(cancellationTokenSource, stopOperationId);
            deferredCompletionSource.TrySetResult(null);
        }
        catch (Exception ex)
        {
            deferredCompletionSource.TrySetException(ex);
        }
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

    private async Task<bool> WaitForServerTaskShutdownAsync(Task? serverTask)
    {
        if (serverTask == null)
        {
            return true;
        }

        try
        {
            var completedTask = await Task.WhenAny(
                serverTask,
                Task.Delay(_shutdownTimeout)).ConfigureAwait(false);
            if (!ReferenceEquals(completedTask, serverTask))
            {
                LogError($"Server task did not complete within {_shutdownTimeout.TotalMilliseconds}ms timeout");
                return false;
            }

            await serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (serverTask.IsCanceled)
        {
            // Cancellation is the normal shutdown path for the server loop.
        }
        catch (Exception ex)
        {
            LogError($"Server task failed during shutdown: {ex.Message}");
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

    private void WaitForStopCompletion(Task stopTask)
    {
        try
        {
            bool completed = stopTask.Wait(_shutdownTimeout);
            if (!completed)
            {
                throw new TimeoutException(
                    $"Timed out after {_shutdownTimeout.TotalMilliseconds}ms while waiting for InspectorHost startup cleanup.");
            }
        }
        catch (AggregateException ex) when (
            stopTask.IsCanceled ||
            ex.Flatten().InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
            // Stop or startup-failure cleanup completed through cancellation.
        }
    }
}
