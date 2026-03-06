using System.Windows;
using WpfDevTools.Inspector.Host;

namespace WpfDevTools.Inspector.Sdk;

/// <summary>
/// Opt-in SDK for WPF DevTools - enables inspection without DLL injection
/// </summary>
public static class InspectorSdk
{
    private static InspectorHost? _host;
    private static int _isInitializing; // 0 = not initializing, 1 = initializing (atomic guard)
    private static volatile bool _isInitialized;

    /// <summary>
    /// Initialize the inspector SDK.
    /// Thread-safe: uses Interlocked.CompareExchange to prevent concurrent initialization.
    /// </summary>
    /// <param name="processId">Process ID (defaults to current process)</param>
    public static void Initialize(int? processId = null)
    {
        if (_isInitialized)
            return;

        // Atomic check-and-set to prevent race condition (same pattern as Bootstrap.cs)
        if (Interlocked.CompareExchange(ref _isInitializing, 1, 0) != 0)
            return;

        try
        {
            if (_isInitialized)
                return;

            var pid = processId ?? Environment.ProcessId;

            // Must run on UI thread
            if (Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        InitializeCore(pid);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isInitializing, 0);
                    }
                });
                return; // finally in Dispatcher.Invoke handles reset
            }

            InitializeCore(pid);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize WpfDevTools Inspector SDK: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isInitializing, 0);
        }
    }

    private static void InitializeCore(int pid)
    {
        _host = new InspectorHost(pid);
        _host.Start();
        _isInitialized = true;
    }

    /// <summary>
    /// Shutdown the inspector SDK.
    /// Thread-safe: uses Interlocked.CompareExchange to prevent concurrent shutdown.
    /// </summary>
    public static void Shutdown()
    {
        if (!_isInitialized)
            return;

        // Atomic guard: reuse _isInitializing to serialize Initialize/Shutdown
        if (Interlocked.CompareExchange(ref _isInitializing, 1, 0) != 0)
            return;

        try
        {
            if (!_isInitialized)
                return;

            _host?.Stop();
            _host = null;
            _isInitialized = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to shutdown WpfDevTools Inspector SDK: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isInitializing, 0);
        }
    }

    /// <summary>
    /// Gets whether the SDK is initialized
    /// </summary>
    public static bool IsInitialized => _isInitialized;
}
