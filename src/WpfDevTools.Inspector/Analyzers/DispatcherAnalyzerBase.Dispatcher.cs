using System.Windows.Threading;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

public abstract partial class DispatcherAnalyzerBase
{
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
        return DispatcherOperationRunner.Invoke(
            targetDispatcher,
            action,
            timeout ?? InspectorConfig.UIThreadTimeout,
            DispatcherRequestContext.CancellationToken,
            "WPF dispatcher operation",
            "Dispatcher operation");
    }
}
