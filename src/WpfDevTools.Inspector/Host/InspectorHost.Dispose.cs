using System.Runtime.ExceptionServices;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Host;

public sealed partial class InspectorHost
{
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
            bool completed = stopCompletionTask.Wait(_shutdownTimeout);
            if (!completed)
            {
                LogError($"Stop finalization did not complete within {_shutdownTimeout.TotalMilliseconds}ms timeout");
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
