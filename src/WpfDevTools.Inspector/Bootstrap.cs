using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.Threading.Tasks;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Shared.Configuration;

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
    private static Shared.Security.AuthenticationManager? _authenticationManager;
    private static Shared.Security.CertificateManager? _certificateManager;
    internal static Action BindingErrorTraceListenerInstallAction { get; set; } = static () => BindingErrorTraceListener.Install();
    internal static Action BindingErrorTraceListenerUninstallAction { get; set; } = static () => BindingErrorTraceListener.Uninstall();
    internal static Func<int, string, Shared.Security.AuthenticationManager?, Shared.Security.CertificateManager?, Host.InspectorHost> HostFactory { get; set; }
        = static (processId, pipeName, authManager, certManager) => new Host.InspectorHost(processId, pipeName, authManager, certManager);
    internal static Action<Host.InspectorHost> HostStartAction { get; set; } = static host => host.Start();
    internal static Action<Host.InspectorHost>? HostStartedCallback { get; set; }
    internal static Action<Shared.Security.AuthenticationManager?>? AuthenticationManagerCreatedCallback { get; set; }
    internal static Func<Dispatcher?> DispatcherResolver { get; set; } = ResolveDispatcher;
    internal static Action<Action> BackgroundInitializationScheduler { get; set; } = static action => _ = Task.Run(action);
    internal static TimeSpan DispatcherFinalizeTimeout { get; set; } = InspectorConfig.ShutdownTimeout;
    internal static Func<bool> FileLogOptInEvaluator { get; set; } = IsTempFileLoggingOptedIn;
    internal static Action<string> FileLogAppendAction { get; set; } = AppendFileLogEntry;

    // Cached once at startup to avoid calling Process.GetCurrentProcess().Id on every opt-in log entry
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

        Dispatcher? dispatcher;

        lock (_lock)
        {
            if (_isInitialized)
            {
                Interlocked.Exchange(ref _isInitializing, 0);
                LogError("Bootstrap already initialized");
                return;
            }

            try
            {
                dispatcher = DispatcherResolver();
                if (dispatcher == null)
                {
                    LogError("Failed to find WPF Application instance");
                    Interlocked.Exchange(ref _isInitializing, 0);
                    return;
                }
            }
            catch (Exception ex)
            {
                LogError($"Bootstrap initialization failed: {ex}");
                Interlocked.Exchange(ref _isInitializing, 0);
                return;
            }
        }

        try
        {
            BackgroundInitializationScheduler(() =>
            {
                try
                {
                    InitializeOnUiThread(parameters, dispatcher);
                }
                catch (Exception ex)
                {
                    LogError($"Failed to initialize inspector: {ex}");
                }
                finally
                {
                    Interlocked.Exchange(ref _isInitializing, 0);
                }
            });
        }
        catch (Exception ex)
        {
            LogError($"Bootstrap background scheduling failed: {ex}");
            Interlocked.Exchange(ref _isInitializing, 0);
        }
    }

    private static Dispatcher? ResolveDispatcher()
    {
        var app = FindWpfApplication();
        return app?.Dispatcher ?? (Application.Current != null ? Dispatcher.CurrentDispatcher : null);
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

    private static void InitializeOnUiThread(string parameters, Dispatcher? dispatcher = null)
    {
        // Parse parameters
        var config = ParseParameters(parameters);

        // Initialize InspectorHost with Named Pipe server
        var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var pipeName = config.TryGetValue("pipeName", out var configuredPipeName) && !string.IsNullOrWhiteSpace(configuredPipeName)
            ? configuredPipeName
            : $"WpfDevTools_{processId}";

        // Create security managers based on parameters
        var authEnabled = config.TryGetValue("auth", out var authVal)
            && string.Equals(authVal, "enabled", StringComparison.OrdinalIgnoreCase);
        var encryptionEnabled = config.TryGetValue("encryption", out var encVal)
            && string.Equals(encVal, "enabled", StringComparison.OrdinalIgnoreCase);
        var authSecretBase64 = config.TryGetValue("authSecretBase64", out var authSecret)
            && !string.IsNullOrWhiteSpace(authSecret)
            ? authSecret
            : null;
        var certDirectory = config.TryGetValue("certDirectory", out var configuredCertDirectory)
            && !string.IsNullOrWhiteSpace(configuredCertDirectory)
            ? configuredCertDirectory
            : null;

        authEnabled = authEnabled || !string.IsNullOrWhiteSpace(authSecretBase64);
        encryptionEnabled = encryptionEnabled || !string.IsNullOrWhiteSpace(certDirectory);

        Shared.Security.AuthenticationManager? authManager = null;
        Shared.Security.CertificateManager? certManager = null;
        Host.InspectorHost? host = null;

        try
        {
            authManager = authEnabled
                ? new Shared.Security.AuthenticationManager(() =>
                    authSecretBase64 ?? Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET"))
                : null;
            AuthenticationManagerCreatedCallback?.Invoke(authManager);

            certManager = encryptionEnabled
                ? (string.IsNullOrWhiteSpace(certDirectory)
                    ? new Shared.Security.CertificateManager()
                    : new Shared.Security.CertificateManager(certDirectory!))
                : null;

            host = HostFactory(processId, pipeName, authManager, certManager);

            var rollbackRequested = 0;

            RunOnDispatcher(dispatcher, () =>
            {
                // Start capturing binding errors only after transport startup succeeds.
                BindingErrorTraceListenerInstallAction();

                if (Volatile.Read(ref rollbackRequested) != 0)
                {
                    BindingErrorTraceListenerUninstallAction();
                    return;
                }

                lock (_lock)
                {
                    if (Volatile.Read(ref rollbackRequested) != 0)
                    {
                        BindingErrorTraceListenerUninstallAction();
                        return;
                    }
                }
            }, () => Interlocked.Exchange(ref rollbackRequested, 1));

            IntegrationTestDelayHooks.DelayBeforeHostStartIfConfigured();
            HostStartAction(host);
            HostStartedCallback?.Invoke(host);

            lock (_lock)
            {
                _host = host;
                _authenticationManager = authManager;
                _certificateManager = certManager;
                _isInitialized = true;
            }

            LogInfo($"Inspector initialized. Pipe: {pipeName}, Auth: {authEnabled}, TLS: {encryptionEnabled}");
        }
        catch (Exception ex)
        {
            try
            {
                CleanupFailedInitialization(host, authManager);
            }
            catch (Exception cleanupError)
            {
                ex.Data["CleanupFailure"] = cleanupError;
                System.Diagnostics.Trace.TraceError($"Bootstrap cleanup after initialization failure also failed: {cleanupError}");
            }

            throw;
        }
    }

    private static void RunOnDispatcher(Dispatcher? dispatcher, Action action, Action? onTimeout = null)
    {
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        var operation = dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
        if (!operation.Task.Wait(DispatcherFinalizeTimeout))
        {
            onTimeout?.Invoke();
            operation.Abort();
            throw new TimeoutException($"Timed out after {DispatcherFinalizeTimeout.TotalMilliseconds}ms while finalizing Bootstrap on the dispatcher thread.");
        }

        operation.Task.GetAwaiter().GetResult();
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
            if (!LooksLikeKeyValueParameters(parameters) && CountOccurrences(parameters, ';') == 1)
            {
                var legacyParts = parameters.Split(new[] { ';' }, 2);
                if (legacyParts.Length == 2)
                {
                    result["inspectorDllPath"] = legacyParts[0].Trim();
                    result["pipeName"] = legacyParts[1].Trim();
                }

                return result;
            }

            var pairs = parameters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var separatorIndex = pair.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = pair.Substring(0, separatorIndex).Trim();
                var value = pair.Substring(separatorIndex + 1).Trim();

                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = value;
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to parse parameters: {ex}");
        }

        return result;
    }

    private static bool LooksLikeKeyValueParameters(string parameters)
    {
        foreach (var pair in parameters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = pair.Trim();
            if (trimmed.StartsWith("inspectorDllPath=", StringComparison.Ordinal) ||
                trimmed.StartsWith("pipeName=", StringComparison.Ordinal) ||
                trimmed.StartsWith("auth=", StringComparison.Ordinal) ||
                trimmed.StartsWith("authSecretBase64=", StringComparison.Ordinal) ||
                trimmed.StartsWith("encryption=", StringComparison.Ordinal) ||
                trimmed.StartsWith("certDirectory=", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountOccurrences(string value, char token)
    {
        var count = 0;
        foreach (var ch in value)
        {
            if (ch == token)
            {
                count++;
            }
        }

        return count;
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
            if (!FileLogOptInEvaluator())
            {
                System.Diagnostics.Debug.WriteLine($"Bootstrap: {message}");
                return;
            }

            FileLogAppendAction($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception logEx)
        {
            System.Diagnostics.Debug.WriteLine($"Bootstrap: Failed to write log file: {logEx.Message}");
        }
    }

    private static void AppendFileLogEntry(string entry)
    {
        File.AppendAllText(_logFilePath, entry);
    }

    private static bool IsTempFileLoggingOptedIn()
    {
        var value = Environment.GetEnvironmentVariable("WPFDEVTOOLS_BOOTSTRAP_FILE_LOG");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || (bool.TryParse(value, out var enabled) && enabled);
    }

    /// <summary>
    /// Check if Inspector is initialized
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    internal static Host.InspectorHost? CurrentHostForTesting => _host;

    internal static void InitializeOnUiThreadForTesting(string parameters)
    {
        InitializeOnUiThread(parameters);
    }

    internal static void ResetForTesting()
    {
        try
        {
            CleanupFailedInitialization(_host, _authenticationManager);
        }
        catch
        {
        }

        _host = null;
        _authenticationManager = null;
        _certificateManager = null;
        _isInitialized = false;
        Interlocked.Exchange(ref _isInitializing, 0);
        BindingErrorTraceListenerInstallAction = static () => BindingErrorTraceListener.Install();
        BindingErrorTraceListenerUninstallAction = static () => BindingErrorTraceListener.Uninstall();
        HostFactory = static (processId, pipeName, authManager, certManager) => new Host.InspectorHost(processId, pipeName, authManager, certManager);
        HostStartAction = static host => host.Start();
        HostStartedCallback = null;
        AuthenticationManagerCreatedCallback = null;
        DispatcherResolver = ResolveDispatcher;
        BackgroundInitializationScheduler = static action => _ = Task.Run(action);
        DispatcherFinalizeTimeout = InspectorConfig.ShutdownTimeout;
        FileLogOptInEvaluator = IsTempFileLoggingOptedIn;
        FileLogAppendAction = AppendFileLogEntry;
    }

    private static void CleanupFailedInitialization(
        Host.InspectorHost? host,
        Shared.Security.AuthenticationManager? authManager)
    {
        Exception? cleanupError = null;
        var hostCleanupFailed = false;

        try
        {
            BindingErrorTraceListenerUninstallAction();
        }
        catch (Exception ex)
        {
            cleanupError = ex;
        }

        try
        {
            host?.Dispose();
        }
        catch (Exception ex)
        {
            hostCleanupFailed = true;
            cleanupError = cleanupError == null
                ? ex
                : new AggregateException(cleanupError, ex);
        }

        try
        {
            if (host == null || hostCleanupFailed || !host.OwnsAuthenticationManager(authManager))
            {
                authManager?.Dispose();
            }
        }
        catch (Exception ex)
        {
            cleanupError = cleanupError == null
                ? ex
                : new AggregateException(cleanupError, ex);
        }

        if (cleanupError != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupError).Throw();
        }
    }
}
