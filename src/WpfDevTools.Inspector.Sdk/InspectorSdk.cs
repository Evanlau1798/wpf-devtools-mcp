using System.Windows;
using WpfDevTools.Inspector.Host;

namespace WpfDevTools.Inspector.Sdk;

/// <summary>
/// Opt-in SDK for WPF DevTools - enables inspection without DLL injection
/// </summary>
public static class InspectorSdk
{
    private static InspectorHost? _host;
    private static bool _isInitialized;

    /// <summary>
    /// Initialize the inspector SDK
    /// </summary>
    /// <param name="processId">Process ID (defaults to current process)</param>
    public static void Initialize(int? processId = null)
    {
        if (_isInitialized)
        {
            return;
        }

        var pid = processId ?? Environment.ProcessId;

        // Must run on UI thread
        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            Application.Current.Dispatcher.Invoke(() => Initialize(processId));
            return;
        }

        try
        {
            _host = new InspectorHost(pid);
            _host.Start();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            // Log error but don't crash the application
            System.Diagnostics.Debug.WriteLine($"Failed to initialize WpfDevTools Inspector SDK: {ex.Message}");
        }
    }

    /// <summary>
    /// Shutdown the inspector SDK
    /// </summary>
    public static void Shutdown()
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
            _host?.Stop();
            _host = null;
            _isInitialized = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to shutdown WpfDevTools Inspector SDK: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets whether the SDK is initialized
    /// </summary>
    public static bool IsInitialized => _isInitialized;
}
