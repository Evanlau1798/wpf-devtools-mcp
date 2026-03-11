using System.Windows;

namespace WpfDevTools.Tests.Unit.TestSupport;

internal sealed class WindowHostScope : IDisposable
{
    private bool _disposed;

    private WindowHostScope(Window window)
    {
        Window = window;
    }

    public Window Window { get; }

    public static WindowHostScope Create(double width = 480, double height = 320)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            ShowInTaskbar = false
        };

        if (Application.Current != null)
        {
            Application.Current.MainWindow = window;
        }

        return new WindowHostScope(window);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Window.Content = null;

        if (Window.IsLoaded)
        {
            Window.Close();
        }

        if (Application.Current?.MainWindow == Window)
        {
            Application.Current.MainWindow = null;
        }
    }
}
