using System.Windows;
using System.Windows.Threading;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Base class for analyzers that need to execute on the UI thread
/// </summary>
public abstract class DispatcherAnalyzerBase
{
    /// <summary>
    /// Execute an action on the UI thread with optional timeout
    /// </summary>
    protected T InvokeOnUIThread<T>(Func<T> action, TimeSpan? timeout = null)
    {
        // If no WPF application context, execute directly
        if (Application.Current == null)
        {
            return action();
        }

        // If already on UI thread, execute directly
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return action();
        }

        // Otherwise, invoke on UI thread with timeout
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);
        return Application.Current.Dispatcher.Invoke(
            action,
            DispatcherPriority.Normal,
            CancellationToken.None,
            actualTimeout);
    }

    /// <summary>
    /// Execute an action on the UI thread (void return) with optional timeout
    /// </summary>
    protected void InvokeOnUIThread(Action action, TimeSpan? timeout = null)
    {
        // If no WPF application context, execute directly
        if (Application.Current == null)
        {
            action();
            return;
        }

        // If already on UI thread, execute directly
        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);
            Application.Current.Dispatcher.Invoke(
                action,
                DispatcherPriority.Normal,
                CancellationToken.None,
                actualTimeout);
        }
    }

    /// <summary>
    /// Check if we're already on the UI thread
    /// </summary>
    protected bool IsOnUIThread()
    {
        return Application.Current?.Dispatcher.CheckAccess() ?? false;
    }
}
