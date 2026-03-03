using System.Windows;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Base class for analyzers that need to execute on the UI thread
/// </summary>
public abstract class DispatcherAnalyzerBase
{
    /// <summary>
    /// Execute an action on the UI thread
    /// </summary>
    protected T InvokeOnUIThread<T>(Func<T> action)
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

        // Otherwise, invoke on UI thread
        return Application.Current.Dispatcher.Invoke(action);
    }

    /// <summary>
    /// Execute an action on the UI thread (void return)
    /// </summary>
    protected void InvokeOnUIThread(Action action)
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
            Application.Current.Dispatcher.Invoke(action);
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
