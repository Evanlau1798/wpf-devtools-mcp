using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WpfDevTools.Shared.Utilities;

internal static class FileLoggerShutdownCoordinator
{
    internal static TimeSpan GetRemainingShutdownTimeout(TimeSpan shutdownTimeout, TimeSpan elapsed)
    {
        var remaining = shutdownTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    internal static Exception? WaitForProcessingTaskShutdown(
        Task processingTask,
        CancellationTokenSource shutdownCts,
        TimeSpan shutdownTimeout)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (processingTask.Wait(GetRemainingShutdownTimeout(shutdownTimeout, stopwatch.Elapsed)))
            {
                return null;
            }

            shutdownCts.Cancel();

            if (processingTask.Wait(GetRemainingShutdownTimeout(shutdownTimeout, stopwatch.Elapsed)))
            {
                return null;
            }

            return CreateShutdownTimeoutException(shutdownTimeout);
        }
        catch (AggregateException ex) when (IsOperationCanceledAggregate(ex))
        {
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    internal static async Task<Exception?> WaitForProcessingTaskShutdownAsync(
        Task processingTask,
        CancellationTokenSource shutdownCts,
        TimeSpan shutdownTimeout)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!processingTask.IsCompleted)
        {
            await ThreadPoolYieldAwaitable.Instance;
        }

        try
        {
            await WaitWithTimeoutAsync(
                processingTask,
                GetRemainingShutdownTimeout(shutdownTimeout, stopwatch.Elapsed)).ConfigureAwait(false);
            return null;
        }
        catch (TimeoutException)
        {
            shutdownCts.Cancel();
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            await WaitWithTimeoutAsync(
                processingTask,
                GetRemainingShutdownTimeout(shutdownTimeout, stopwatch.Elapsed)).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (TimeoutException ex)
        {
            return CreateShutdownTimeoutException(shutdownTimeout, ex);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task WaitWithTimeoutAsync(Task task, TimeSpan timeout)
    {
        if (task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return;
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new TimeoutException();
        }

        var completedTask = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (ReferenceEquals(completedTask, task))
        {
            await task.ConfigureAwait(false);
            return;
        }

        throw new TimeoutException();
    }

    private static bool IsOperationCanceledAggregate(AggregateException ex)
    {
        return ex.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException);
    }

    private static TimeoutException CreateShutdownTimeoutException(
        TimeSpan shutdownTimeout,
        Exception? innerException = null)
    {
        var message = $"FileLogger shutdown timed out after {shutdownTimeout.TotalMilliseconds:0}ms " +
            "and the background writer did not stop after cancellation.";

        return innerException is null
            ? new TimeoutException(message)
            : new TimeoutException(message, innerException);
    }

    private readonly struct ThreadPoolYieldAwaitable
    {
        internal static ThreadPoolYieldAwaitable Instance => default;

        public ThreadPoolYieldAwaiter GetAwaiter() => default;
    }

    private readonly struct ThreadPoolYieldAwaiter : ICriticalNotifyCompletion
    {
        public bool IsCompleted => false;

        public void GetResult()
        {
        }

        public void OnCompleted(Action continuation) => QueueContinuation(continuation);

        public void UnsafeOnCompleted(Action continuation) => QueueContinuation(continuation);

        private static void QueueContinuation(Action continuation)
        {
            ThreadPool.QueueUserWorkItem(static state => ((Action)state!).Invoke(), continuation);
        }
    }
}
