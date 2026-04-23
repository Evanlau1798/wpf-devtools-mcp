using System.Windows;
using System.Windows.Threading;
using WpfDevTools.Inspector.Host;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Security;

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

    internal static Func<Dispatcher?> DispatcherResolver { get; set; } = static () => Application.Current?.Dispatcher;
    internal static Action? InitializationQueuedCallback { get; set; }
    internal static Action? BeforeAbortPendingInitializationCallback { get; set; }
    internal static Action<InspectorHost>? HostStartedCallback { get; set; }

    public static Exception? LastInitializationError { get; private set; }
    public static Exception? LastShutdownError { get; private set; }

    /// <summary>
    /// Initialize the inspector SDK.
    /// Thread-safe: uses Interlocked.CompareExchange to prevent concurrent initialization.
    /// </summary>
    /// <param name="processId">Process ID (defaults to current process)</param>
    public static void Initialize(int? processId = null)
    {
        LastInitializationError = null;
        LastShutdownError = null;

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
            var dispatcher = DispatcherResolver();
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                InvokeInitializeOnDispatcher(dispatcher, pid);
                return;
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
            HostStartedCallback?.Invoke(host);

            _authenticationManager = authenticationManager;
            _certificateManager = certificateManager;
            _host = host;
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            try
            {
                CleanupHostResources(host, authenticationManager);
            }
            catch (Exception cleanupError)
            {
                ex.Data["CleanupFailure"] = cleanupError;
                Trace.TraceError($"InspectorSdk cleanup after initialization failure also failed: {cleanupError}");
            }

            ExceptionDispatchInfo.Capture(ex).Throw();
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
        LastShutdownError = null;

        if (!_isInitialized)
            return;

        // Atomic guard: reuse _isInitializing to serialize Initialize/Shutdown
        if (Interlocked.CompareExchange(ref _isInitializing, 1, 0) != 0)
            return;

        try
        {
            if (!_isInitialized)
                return;

            var host = _host;
            var authenticationManager = _authenticationManager;

            _host = null;
            _authenticationManager = null;
            _certificateManager = null;
            _isInitialized = false;

            CleanupHostResources(host, authenticationManager);
        }
        catch (Exception ex)
        {
            LastShutdownError = ex;
            System.Diagnostics.Debug.WriteLine($"Failed to shutdown WpfDevTools Inspector SDK: {ex.Message}");
            Trace.TraceError($"Failed to shutdown WpfDevTools Inspector SDK: {ex}");
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

    internal static void CleanupHostResources(InspectorHost? host, AuthenticationManager? authenticationManager)
    {
        Exception? cleanupError = null;
        var hostCleanupFailed = false;

        try
        {
            host?.Dispose();
        }
        catch (Exception ex)
        {
            hostCleanupFailed = true;
            cleanupError = ex;
        }

        try
        {
            if (host == null || hostCleanupFailed || !host.OwnsAuthenticationManager(authenticationManager))
            {
                authenticationManager?.Dispose();
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
            ExceptionDispatchInfo.Capture(cleanupError).Throw();
        }
    }

    internal static void InvokeInitializeOnDispatcher(
        Dispatcher dispatcher,
        int processId,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException("Cannot initialize WpfDevTools Inspector SDK because the UI dispatcher is shutting down.");
        }

        var actualTimeout = timeout ?? InspectorConfig.UIThreadTimeout;
        var initializationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = dispatcher.InvokeAsync(
            () =>
            {
                initializationStarted.TrySetResult(null);
                InitializeCore(processId);
            },
            DispatcherPriority.Normal,
            CancellationToken.None);
        InitializationQueuedCallback?.Invoke();

        var completedTask = Task.WhenAny(
            initializationStarted.Task,
            operation.Task,
            Task.Delay(actualTimeout)).GetAwaiter().GetResult();

        if (ReferenceEquals(completedTask, operation.Task))
        {
            if (operation.Status == DispatcherOperationStatus.Aborted || operation.Task.IsCanceled ||
                dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                throw new InvalidOperationException("Cannot initialize WpfDevTools Inspector SDK because the UI dispatcher shut down before initialization could start.");
            }

            operation.Task.GetAwaiter().GetResult();
            return;
        }

        if (ReferenceEquals(completedTask, initializationStarted.Task))
        {
            operation.Task.GetAwaiter().GetResult();
            return;
        }

        if (initializationStarted.Task.IsCompleted || operation.Status == DispatcherOperationStatus.Executing)
        {
            operation.Task.GetAwaiter().GetResult();
            return;
        }

        if (operation.Status == DispatcherOperationStatus.Aborted || operation.Task.IsCanceled ||
            dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException("Cannot initialize WpfDevTools Inspector SDK because the UI dispatcher shut down before initialization could start.");
        }

        if (operation.Status == DispatcherOperationStatus.Pending)
        {
            BeforeAbortPendingInitializationCallback?.Invoke();

            if (initializationStarted.Task.IsCompleted || operation.Status == DispatcherOperationStatus.Executing || operation.Task.IsCompleted)
            {
                operation.Task.GetAwaiter().GetResult();
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                throw new InvalidOperationException("Cannot initialize WpfDevTools Inspector SDK because the UI dispatcher shut down before initialization could start.");
            }

            operation.Abort();

            if (initializationStarted.Task.IsCompleted || operation.Status == DispatcherOperationStatus.Executing)
            {
                operation.Task.GetAwaiter().GetResult();
                return;
            }

            if (operation.Status == DispatcherOperationStatus.Aborted || operation.Task.IsCanceled)
            {
                throw new TimeoutException(
                    $"Timed out after {actualTimeout.TotalMilliseconds}ms while marshaling WpfDevTools Inspector SDK initialization to the UI dispatcher.");
            }

            if (operation.Task.IsCompleted)
            {
                operation.Task.GetAwaiter().GetResult();
                return;
            }
        }

        operation.Task.GetAwaiter().GetResult();
    }
}
