using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Fixture that provides a WPF Application context for integration tests
/// </summary>
public class WpfApplicationFixture : IDisposable
{
    private static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly Thread _uiThread;
    private readonly ManualResetEventSlim _appStarted = new ManualResetEventSlim(false);
    private readonly ManualResetEventSlim _appStopped = new ManualResetEventSlim(false);
    private Application? _application;
    private Dispatcher? _dispatcher;
    private Window? _rootWindow;

    public WpfApplicationFixture()
    {
        // Create UI thread with STA apartment state
        _uiThread = new Thread(() =>
        {
            try
            {
                // Create WPF Application
                _application = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };

                _dispatcher = Dispatcher.CurrentDispatcher;

                _rootWindow = CreateHiddenRootWindow();
                _application.MainWindow = _rootWindow;

                // Signal that app is ready
                _appStarted.Set();

                // Run dispatcher
                Dispatcher.Run();
            }
            finally
            {
                _appStopped.Set();
            }
        });

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();

        // Wait for application to start
        _appStarted.Wait(DefaultStartupTimeout);

        if (_dispatcher == null)
        {
            throw new InvalidOperationException("Failed to initialize WPF Application");
        }
    }

    /// <summary>
    /// Run action on UI thread and return result
    /// </summary>
    public T RunOnUIThread<T>(Func<T> action)
    {
        if (_dispatcher == null)
        {
            throw new InvalidOperationException("Dispatcher not initialized");
        }

        return _dispatcher.Invoke(() =>
        {
            EnsureMainWindow();
            return action();
        });
    }

    /// <summary>
    /// Run action on UI thread
    /// </summary>
    public void RunOnUIThread(Action action)
    {
        if (_dispatcher == null)
        {
            throw new InvalidOperationException("Dispatcher not initialized");
        }

        _dispatcher.Invoke(() =>
        {
            EnsureMainWindow();
            action();
        });
    }

    /// <summary>
    /// Run async action on UI thread and return result.
    /// </summary>
    public Task<T> RunOnUIThreadAsync<T>(Func<Task<T>> action)
    {
        if (_dispatcher == null)
        {
            throw new InvalidOperationException("Dispatcher not initialized");
        }

        return _dispatcher.InvokeAsync(() =>
        {
            EnsureMainWindow();
            return action();
        }).Task.Unwrap();
    }

    private void EnsureMainWindow()
    {
        if (_application == null)
        {
            throw new InvalidOperationException("Application not initialized");
        }

        if (Application.Current?.MainWindow != null)
        {
            return;
        }

        _rootWindow = CreateHiddenRootWindow();
        _application.MainWindow = _rootWindow;
    }

    private static Window CreateHiddenRootWindow() =>
        new Window
        {
            Width = 800,
            Height = 600,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Visibility = Visibility.Hidden
        };

    internal static bool CompleteShutdown(Thread uiThread, ManualResetEventSlim appStopped, TimeSpan shutdownTimeout)
    {
        if (uiThread.IsAlive && shutdownTimeout > TimeSpan.Zero)
        {
            var stopwatch = Stopwatch.StartNew();
            var remainingTimeout = GetRemainingTimeout(shutdownTimeout, stopwatch);
            if (remainingTimeout > TimeSpan.Zero)
            {
                appStopped.Wait(remainingTimeout);
            }

            remainingTimeout = GetRemainingTimeout(shutdownTimeout, stopwatch);
            if (uiThread.IsAlive && remainingTimeout > TimeSpan.Zero)
            {
                uiThread.Join(remainingTimeout);
            }
        }

        return !uiThread.IsAlive;
    }

    internal static void ReleaseStoppedSignal(bool shutdownCompleted, ManualResetEventSlim appStopped)
    {
        if (shutdownCompleted)
        {
            appStopped.Dispose();
            return;
        }

        ScheduleDeferredSignalDisposal(appStopped);
    }

    private static TimeSpan GetRemainingTimeout(TimeSpan timeout, Stopwatch stopwatch)
    {
        var remainingTimeout = timeout - stopwatch.Elapsed;
        return remainingTimeout > TimeSpan.Zero
            ? remainingTimeout
            : TimeSpan.Zero;
    }

    private static void ScheduleDeferredSignalDisposal(ManualResetEventSlim appStopped)
    {
        _ = ThreadPool.RegisterWaitForSingleObject(
            appStopped.WaitHandle,
            static (state, _) =>
            {
                try
                {
                    ((ManualResetEventSlim)state!).Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            },
            appStopped,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    public void Dispose()
    {
        if (_dispatcher != null)
        {
            _dispatcher.InvokeShutdown();
        }

        var shutdownCompleted = CompleteShutdown(_uiThread, _appStopped, DefaultShutdownTimeout);
        ReleaseStoppedSignal(shutdownCompleted, _appStopped);

        _appStarted.Dispose();
    }
}
