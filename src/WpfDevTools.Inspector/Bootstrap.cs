using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using System.IO;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector;

/// <summary>
/// Bootstrap entry point for Inspector DLL
/// Called when DLL is injected into target WPF process
/// Excluded from code coverage: requires real DLL injection into a WPF process
/// </summary>
[ExcludeFromCodeCoverage]
public static class Bootstrap
{
    private static volatile bool _isInitialized;
    private static int _isInitializing; // 0 = not initializing, 1 = initializing
    private static readonly object _lock = new object();
    private static Host.InspectorHost? _host;

    // Cached once at startup to avoid calling Process.GetCurrentProcess().Id on every log entry
    private static readonly string _logFilePath = Path.Combine(
        Path.GetTempPath(),
        $"WpfDevTools_Inspector_{System.Diagnostics.Process.GetCurrentProcess().Id}.log");

    /// <summary>
    /// Initialize the Inspector in the target WPF application
    /// This method is called from the injected DLL
    /// </summary>
    public static void Initialize(string parameters)
    {
        // Atomic check-and-set to prevent race condition
        if (Interlocked.CompareExchange(ref _isInitializing, 1, 0) != 0)
        {
            LogError("Bootstrap already initializing");
            return;
        }

        lock (_lock)
        {
            if (_isInitialized)
            {
                Interlocked.Exchange(ref _isInitializing, 0);
                LogError("Bootstrap already initialized");
                return;
            }

            var beginInvokeCalled = false;

            try
            {
                // Find WPF Application instance
                var app = FindWpfApplication();
                if (app == null)
                {
                    LogError("Failed to find WPF Application instance");
                    return; // finally resets _isInitializing
                }

                // Marshal to UI thread
                var dispatcher = app.Dispatcher ?? Dispatcher.CurrentDispatcher;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        InitializeOnUiThread(parameters);
                        lock (_lock)
                        {
                            _isInitialized = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to initialize on UI thread: {ex}");
                    }
                    finally
                    {
                        // Reset _isInitializing only after UI thread work completes
                        Interlocked.Exchange(ref _isInitializing, 0);
                    }
                }), DispatcherPriority.Normal);

                // BeginInvoke was called successfully - the callback's finally will reset _isInitializing
                beginInvokeCalled = true;
            }
            catch (Exception ex)
            {
                LogError($"Bootstrap initialization failed: {ex}");
            }
            finally
            {
                // Reset _isInitializing only if BeginInvoke was NOT called.
                // When BeginInvoke succeeded, the callback's finally handles the reset.
                if (!beginInvokeCalled)
                {
                    Interlocked.Exchange(ref _isInitializing, 0);
                }
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
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var appType = assembly.GetType("System.Windows.Application");
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
                catch (Exception assemblyEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Bootstrap: Failed to search assembly for Application type: {assemblyEx.Message}");
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

        // Create security managers based on parameters
        var authEnabled = config.TryGetValue("auth", out var authVal)
            && string.Equals(authVal, "enabled", StringComparison.OrdinalIgnoreCase);
        var encryptionEnabled = config.TryGetValue("encryption", out var encVal)
            && string.Equals(encVal, "enabled", StringComparison.OrdinalIgnoreCase);

        var authManager = authEnabled
            ? new Shared.Security.AuthenticationManager()
            : null;
        var certManager = encryptionEnabled
            ? new Shared.Security.CertificateManager()
            : null;

        _host = new Host.InspectorHost(processId, authManager, certManager);
        _host.Start();

        // Start capturing binding errors immediately after injection
        BindingErrorTraceListener.Install();

        LogInfo($"Inspector initialized. Pipe: WpfDevTools_{processId}, Auth: {authEnabled}, TLS: {encryptionEnabled}");
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
            File.AppendAllText(_logFilePath, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception logEx)
        {
            System.Diagnostics.Debug.WriteLine($"Bootstrap: Failed to write log file: {logEx.Message}");
        }
    }

    /// <summary>
    /// Check if Inspector is initialized
    /// </summary>
    public static bool IsInitialized => _isInitialized;
}
