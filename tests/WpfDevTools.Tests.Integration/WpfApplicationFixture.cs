using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Fixture that provides a WPF Application context for integration tests
/// </summary>
public class WpfApplicationFixture : IDisposable
{
    private readonly Thread _uiThread;
    private readonly ManualResetEventSlim _appStarted = new ManualResetEventSlim(false);
    private Application? _application;
    private Dispatcher? _dispatcher;

    public WpfApplicationFixture()
    {
        // Create UI thread with STA apartment state
        _uiThread = new Thread(() =>
        {
            // Create WPF Application
            _application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            _dispatcher = Dispatcher.CurrentDispatcher;

            // Create a simple window to ensure Application.Current.MainWindow exists
            var window = new Window
            {
                Width = 800,
                Height = 600,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Visibility = Visibility.Hidden
            };

            _application.MainWindow = window;

            // Signal that app is ready
            _appStarted.Set();

            // Run dispatcher
            Dispatcher.Run();
        });

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();

        // Wait for application to start
        _appStarted.Wait(TimeSpan.FromSeconds(10));

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

        return _dispatcher.Invoke(action);
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

        _dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (_dispatcher != null)
        {
            _dispatcher.InvokeShutdown();
        }

        if (_uiThread.IsAlive)
        {
            _uiThread.Join(TimeSpan.FromSeconds(5));
        }

        _appStarted.Dispose();
    }
}
