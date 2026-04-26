using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

public abstract partial class DispatcherAnalyzerBase
{
    private const int DispatcherWaitPollMilliseconds = 25;

    /// <summary>
    /// Execute an action on the specified dispatcher with optional timeout.
    /// Returns a structured unavailable result for object-returning analyzer calls.
    /// </summary>
    protected T InvokeOnDispatcher<T>(Dispatcher? dispatcher, Func<T> action, TimeSpan? timeout = null)
    {
        if (TryGetDispatcherUnavailableMessage(dispatcher, out var unavailableMessage))
        {
            return CreateDispatcherUnavailableResult<T>(unavailableMessage);
        }

        var targetDispatcher = dispatcher!;
        bool hasAccess;
        try
        {
            hasAccess = targetDispatcher.CheckAccess();
        }
        catch (InvalidOperationException ex)
        {
            return CreateDispatcherUnavailableResult<T>(
                "WPF dispatcher unavailable because access could not be validated; analyzer action was not executed.",
                ex);
        }

        if (hasAccess)
        {
            return action();
        }

        try
        {
            return InvokeOnDispatcherWithRequestCancellation(targetDispatcher, action, timeout);
        }
        catch (InvalidOperationException ex) when (IsDispatcherShuttingDown(targetDispatcher))
        {
            return CreateDispatcherUnavailableResult<T>(
                "WPF dispatcher unavailable because it is shutting down; analyzer action was not executed.",
                ex);
        }
    }

    /// <summary>
    /// Execute a void action on the specified dispatcher with optional timeout.
    /// Throws a clear dispatcher unavailable error when the action cannot be marshalled safely.
    /// </summary>
    protected void InvokeOnDispatcher(Dispatcher? dispatcher, Action action, TimeSpan? timeout = null)
    {
        if (TryGetDispatcherUnavailableMessage(dispatcher, out var unavailableMessage))
        {
            throw CreateDispatcherUnavailableException(unavailableMessage);
        }

        var targetDispatcher = dispatcher!;
        bool hasAccess;
        try
        {
            hasAccess = targetDispatcher.CheckAccess();
        }
        catch (InvalidOperationException ex)
        {
            throw CreateDispatcherUnavailableException(
                "WPF dispatcher unavailable because access could not be validated; analyzer action was not executed.",
                ex);
        }

        if (hasAccess)
        {
            action();
            return;
        }

        try
        {
            InvokeOnDispatcherWithRequestCancellation<object?>(targetDispatcher, () =>
            {
                action();
                return null;
            }, timeout);
        }
        catch (InvalidOperationException ex) when (IsDispatcherShuttingDown(targetDispatcher))
        {
            throw CreateDispatcherUnavailableException(
                "WPF dispatcher unavailable because it is shutting down; analyzer action was not executed.",
                ex);
        }
    }

    private static T InvokeOnDispatcherWithRequestCancellation<T>(
        Dispatcher targetDispatcher,
        Func<T> action,
        TimeSpan? timeout)
    {
        DispatcherOperation? operation = null;
        ExceptionDispatchInfo? capturedException = null;
        T? result = default;
        var started = 0;
        var completed = 0;
        using var completion = new ManualResetEventSlim(false);

        operation = targetDispatcher.BeginInvoke(new Action(() =>
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
                completion.Set();
            }
        }), DispatcherPriority.Normal);

        WaitForDispatcherOperation(
            operation,
            completion,
            () => Volatile.Read(ref started) != 0,
            () => Volatile.Read(ref completed) != 0,
            timeout ?? InspectorConfig.UIThreadTimeout,
            DispatcherRequestContext.CancellationToken);

        capturedException?.Throw();
        return result!;
    }

    private static void WaitForDispatcherOperation(
        DispatcherOperation operation,
        ManualResetEventSlim completion,
        Func<bool> hasStarted,
        Func<bool> hasCompleted,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var timeoutAt = DateTime.UtcNow + timeout;
        while (true)
        {
            if (completion.Wait(GetPollDelay(timeoutAt)))
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                AbortPendingOperation(operation, hasStarted);
                throw new OperationCanceledException(
                    "Dispatcher operation was canceled before it completed.",
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
                    $"Timed out waiting for WPF dispatcher operation after {timeout.TotalMilliseconds:0}ms.");
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

        var pollDelay = TimeSpan.FromMilliseconds(DispatcherWaitPollMilliseconds);
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
