using System.Windows;
using WpfDevTools.Inspector.Host;
using System.Diagnostics;

namespace WpfDevTools.Inspector.Sdk;

/// <summary>
/// Opt-in SDK for WPF DevTools - enables inspection without DLL injection
/// </summary>
public static class InspectorSdk
{
    private static InspectorHost? _host;
    private static WpfDevTools.Shared.Security.AuthenticationManager? _authenticationManager;
    private static WpfDevTools.Shared.Security.CertificateManager? _certificateManager;
    private static int _isInitializing; // 0 = not initializing, 1 = initializing (atomic guard)
    private static volatile bool _isInitialized;

    public static Exception? LastInitializationError { get; private set; }

    /// <summary>
    /// Initialize the inspector SDK.
    /// Thread-safe: uses Interlocked.CompareExchange to prevent concurrent initialization.
    /// </summary>
    /// <param name="processId">Process ID (defaults to current process)</param>
    public static void Initialize(int? processId = null)
    {
        LastInitializationError = null;

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
            LastInitializationError = ex;
            System.Diagnostics.Debug.WriteLine($"Failed to initialize WpfDevTools Inspector SDK: {ex.Message}");
            Trace.TraceError($"Failed to initialize WpfDevTools Inspector SDK: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _isInitializing, 0);
        }
    }

    private static void InitializeCore(int pid)
    {
        var transportSecurity = InspectorSdkTransportSecurityConfiguration.Create(
            Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET"),
            Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR"));
        var authenticationManager = transportSecurity.AuthenticationManager;
        var certificateManager = transportSecurity.CertificateManager;
        InspectorHost? host = null;

        try
        {
            host = new InspectorHost(pid, authenticationManager, certificateManager);
            host.Start();

            _authenticationManager = authenticationManager;
            _certificateManager = certificateManager;
            _host = host;
            _isInitialized = true;
        }
        catch
        {
            host?.Stop();
            authenticationManager?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Shutdown the inspector SDK.
    /// Thread-safe: uses Interlocked.CompareExchange to prevent concurrent shutdown.
    /// </summary>
    public static void Shutdown()
    {
        LastInitializationError = null;

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
            _authenticationManager?.Dispose();
            _authenticationManager = null;
            _certificateManager = null;
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
