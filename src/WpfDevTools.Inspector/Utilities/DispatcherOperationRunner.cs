using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace WpfDevTools.Inspector.Utilities;

internal static class DispatcherOperationRunner
{
    private const int PollMilliseconds = 25;

    internal static T Invoke<T>(
        Dispatcher dispatcher,
        Func<T> action,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string operationDescription,
        string? cancellationDescription = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DispatcherOperation? operation = null;
        ExceptionDispatchInfo? capturedException = null;
        T? result = default;
        var started = 0;
        var completed = 0;
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        operation = dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref started, 1);
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Interlocked.Exchange(ref completed, 1);
                completion.TrySetResult(null);
            }
        }), DispatcherPriority.Normal);

        WaitForOperation(
            operation,
            completion,
            () => Volatile.Read(ref started) != 0,
            () => Volatile.Read(ref completed) != 0,
            timeout,
            cancellationToken,
            operationDescription,
            cancellationDescription ?? operationDescription);

        capturedException?.Throw();
        return result!;
    }

    private static void WaitForOperation(
        DispatcherOperation operation,
        TaskCompletionSource<object?> completion,
        Func<bool> hasStarted,
        Func<bool> hasCompleted,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string operationDescription,
        string cancellationDescription)
    {
        var timeoutAt = DateTime.UtcNow + timeout;
        while (true)
        {
            if (completion.Task.Wait(GetPollDelay(timeoutAt)))
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                AbortPendingOperation(operation, hasStarted);
                throw new OperationCanceledException(
                    $"{cancellationDescription} was canceled before it completed.",
                    cancellationToken);
            }

            if (DateTime.UtcNow < timeoutAt)
            {
                continue;
            }

            AbortPendingOperation(operation, hasStarted);
            if (!hasCompleted())
            {
                throw new TimeoutException(
                    $"Timed out waiting for {operationDescription} after {timeout.TotalMilliseconds:0}ms.");
            }
        }
    }

    private static TimeSpan GetPollDelay(DateTime timeoutAt)
    {
        var remaining = timeoutAt - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var pollDelay = TimeSpan.FromMilliseconds(PollMilliseconds);
        return remaining < pollDelay ? remaining : pollDelay;
    }

    private static void AbortPendingOperation(DispatcherOperation operation, Func<bool> hasStarted)
    {
        if (hasStarted())
        {
            return;
        }

        try
        {
            operation.Abort();
        }
        catch (InvalidOperationException)
        {
            // Dispatcher is already shutting down or the operation state changed.
        }
    }
}
