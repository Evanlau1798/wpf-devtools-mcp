using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace WpfDevTools.Inspector;

/// <summary>
/// Bootstrap entry point for Inspector DLL
/// Called when DLL is injected into target WPF process
/// </summary>
public static class Bootstrap
{
    private static bool _isInitialized;
    private static readonly object _lock = new object();

    /// <summary>
    /// Initialize the Inspector in the target WPF application
    /// This method is called from the injected DLL
    /// </summary>
    public static void Initialize(string parameters)
    {
        lock (_lock)
        {
            if (_isInitialized)
            {
                LogError("Bootstrap already initialized");
                return;
            }

            try
            {
                // Find WPF Application instance
                var app = FindWpfApplication();
                if (app == null)
                {
                    LogError("Failed to find WPF Application instance");
                    return;
                }

                // Marshal to UI thread
                var dispatcher = app.Dispatcher ?? Dispatcher.CurrentDispatcher;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        InitializeOnUiThread(parameters);
                        _isInitialized = true;
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to initialize on UI thread: {ex}");
                    }
                }), DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                LogError($"Bootstrap initialization failed: {ex}");
            }
        }
    }

    private static Application? FindWpfApplication()
    {
        try
        {
            // Try Application.Current first
            if (Application.Current != null)
            {
                return Application.Current;
            }

            // Fallback: search AppDomains (.NET Framework)
#if NET48
            foreach (var domain in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var appType = domain.GetType("System.Windows.Application");
                    if (appType != null)
                    {
                        var currentProp = appType.GetProperty("Current");
                        var app = currentProp?.GetValue(null) as Application;
                        if (app != null)
                        {
                            return app;
                        }
                    }
                }
                catch
                {
                    // Continue searching
                }
            }
#endif
        }
        catch (Exception ex)
        {
            LogError($"Error finding WPF application: {ex}");
        }

        return null;
    }

    private static void InitializeOnUiThread(string parameters)
    {
        // Parse parameters
        var config = ParseParameters(parameters);

        // Initialize InspectorHost with Named Pipe server
        var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var host = new Host.InspectorHost(processId);
        host.Start();

        LogInfo($"Inspector initialized successfully. Named Pipe: WpfDevTools_{processId}");
    }

    private static Dictionary<string, string> ParseParameters(string parameters)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(parameters))
        {
            return result;
        }

        try
        {
            var pairs = parameters.Split(';');
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    result[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to parse parameters: {ex}");
        }

        return result;
    }

    private static void LogInfo(string message)
    {
        LogToFile($"[INFO] {message}");
    }

    private static void LogError(string message)
    {
        LogToFile($"[ERROR] {message}");
    }

    private static void LogToFile(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Path.GetTempPath(),
                $"WpfDevTools_Inspector_{System.Diagnostics.Process.GetCurrentProcess().Id}.log");

            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging errors
        }
    }

    /// <summary>
    /// Check if Inspector is initialized
    /// </summary>
    public static bool IsInitialized => _isInitialized;
}
