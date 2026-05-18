using System.Windows;
using System.Windows.Threading;
using WpfDevTools.Inspector.Host;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Sdk;

public sealed record InspectorSdkInitializationStatus(
    string State,
    bool IsInitialized,
    int? ProcessId,
    string? ErrorCode,
    string? ErrorType,
    string? ErrorMessage,
    string? Hint,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// Opt-in SDK for WPF DevTools - enables inspection without DLL injection
/// </summary>
public static partial class InspectorSdk
{
    private static InspectorHost? _host;
    private static WpfDevTools.Shared.Security.AuthenticationManager? _authenticationManager;
    private static WpfDevTools.Shared.Security.CertificateManager? _certificateManager;
    private static int _isInitializing; // 0 = not initializing, 1 = initializing (atomic guard)
    private static int _shutdownRequestedDuringInitialization;
    private static readonly object LifecycleLock = new();
    private static volatile bool _isInitialized;

    internal static Func<Dispatcher?> DispatcherResolver { get; set; } = static () => Application.Current?.Dispatcher;
    internal static Action? InitializationQueuedCallback { get; set; }
    internal static Action? BeforeAbortPendingInitializationCallback { get; set; }
    internal static Action<InspectorHost>? HostStartedCallback { get; set; }

    public static Exception? LastInitializationError { get; private set; }
    public static Exception? LastShutdownError { get; private set; }
    public static InspectorSdkInitializationStatus LastInitializationStatus { get; private set; } = CreateNotStartedStatus();

    /// <summary>
    /// Initialize the inspector SDK.
    /// Thread-safe: uses Interlocked.CompareExchange to prevent concurrent initialization.
    /// </summary>
    /// <param name="processId">Process ID (defaults to current process)</param>
    public static void Initialize(int? processId = null)
    {
        LastInitializationError = null;
        LastShutdownError = null;
        var pid = processId ?? Environment.ProcessId;
        LastInitializationStatus = CreateInitializingStatus(pid);

        if (_isInitialized)
        {
            LastInitializationStatus = CreateInitializedStatus(pid);
            return;
        }

        // Atomic check-and-set to prevent race condition (same pattern as Bootstrap.cs)
        if (Interlocked.CompareExchange(ref _isInitializing, 1, 0) != 0)
            return;

        try
        {
            if (_isInitialized)
            {
                LastInitializationStatus = CreateInitializedStatus(pid);
                return;
            }

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
            LastInitializationStatus = CreateFailedStatus(pid, ex);
            System.Diagnostics.Debug.WriteLine($"Failed to initialize WpfDevTools Inspector SDK: {ex.Message}");
            Trace.TraceError($"Failed to initialize WpfDevTools Inspector SDK: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _isInitializing, 0);
            RunDeferredShutdownIfRequested();
        }
    }

    private static void InitializeCore(int pid, InitializationDeadline? deadline = null)
    {
        AuthenticationManager? authenticationManager = null;
        CertificateManager? certificateManager = null;
        InspectorHost? host = null;

        try
        {
            deadline?.ThrowIfExpired();

            var transportSecurity = InspectorSdkTransportSecurityConfiguration.Create(
                Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET"),
                Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR"));
            authenticationManager = transportSecurity.AuthenticationManager;
            certificateManager = transportSecurity.CertificateManager;

            deadline?.ThrowIfExpired();

            host = deadline == null
                ? new InspectorHost(pid, authenticationManager, certificateManager)
                : new InspectorHost(pid, authenticationManager, certificateManager, deadline.GetRemainingTimeoutOrThrow());
            host.Start();
            HostStartedCallback?.Invoke(host);

            if (DisposeStartedHostIfShutdownRequested(ref host, ref authenticationManager, ref certificateManager))
            {
                return;
            }

            if (deadline?.TryClaimPublish() == false)
            {
                throw deadline.CreateCompletionTimeoutException();
            }

            if (!TryPublishInitializedHost(pid, ref host, ref authenticationManager, ref certificateManager))
            {
                DisposeStartedHostIfShutdownRequested(ref host, ref authenticationManager, ref certificateManager);
                return;
            }
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
        if (TryRecordShutdownDuringInitialization())
        {
            return;
        }

        // Atomic guard: reuse _isInitializing to serialize Initialize/Shutdown
        if (Interlocked.CompareExchange(ref _isInitializing, 1, 0) != 0)
        {
            RecordShutdownRequestedDuringInitialization();
            return;
        }

        LastInitializationError = null;
        LastShutdownError = null;

        try
        {
            if (!TryTakePublishedHostForShutdown(out var host, out var authenticationManager))
            {
                return;
            }

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

        var deadline = new InitializationDeadline(timeout ?? InspectorConfig.UIThreadTimeout);
        var actualTimeout = deadline.Timeout;
        var initializationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = dispatcher.InvokeAsync(
            () =>
            {
                initializationStarted.TrySetResult(null);
                InitializeCore(processId, deadline);
            },
            DispatcherPriority.Normal,
            CancellationToken.None);
        InitializationQueuedCallback?.Invoke();

        var completedTask = Task.WhenAny(
            initializationStarted.Task,
            operation.Task,
            Task.Delay(deadline.GetRemainingTimeout())).GetAwaiter().GetResult();

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
            WaitForOperationCompletionOrThrow(operation.Task, deadline);
            return;
        }

        if (initializationStarted.Task.IsCompleted || operation.Status == DispatcherOperationStatus.Executing)
        {
            WaitForOperationCompletionOrThrow(operation.Task, deadline);
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
                WaitForOperationCompletionOrThrow(operation.Task, deadline);
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                throw new InvalidOperationException("Cannot initialize WpfDevTools Inspector SDK because the UI dispatcher shut down before initialization could start.");
            }

            operation.Abort();

            if (initializationStarted.Task.IsCompleted || operation.Status == DispatcherOperationStatus.Executing)
            {
                WaitForOperationCompletionOrThrow(operation.Task, deadline);
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

        WaitForOperationCompletionOrThrow(operation.Task, deadline);
    }

    private static void WaitForOperationCompletionOrThrow(
        Task operationTask,
        InitializationDeadline deadline)
    {
        if (operationTask.IsCompleted)
        {
            operationTask.GetAwaiter().GetResult();
            return;
        }

        var remaining = deadline.GetRemainingTimeout();
        if (remaining > TimeSpan.Zero)
        {
            var completedTask = Task.WhenAny(operationTask, Task.Delay(remaining)).GetAwaiter().GetResult();
            if (ReferenceEquals(completedTask, operationTask))
            {
                operationTask.GetAwaiter().GetResult();
                return;
            }
        }

        if (!deadline.TryMarkTimedOut())
        {
            operationTask.GetAwaiter().GetResult();
            return;
        }

        ObserveLateDispatcherOperationFailure(operationTask);
        throw deadline.CreateCompletionTimeoutException();
    }

    private static void ObserveLateDispatcherOperationFailure(Task operationTask)
    {
        _ = operationTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed class InitializationDeadline
    {
        private readonly object _lock = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _published;
        private bool _timedOut;

        public InitializationDeadline(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        public TimeSpan Timeout { get; }

        public TimeSpan GetRemainingTimeout()
        {
            lock (_lock)
            {
                if (_timedOut)
                {
                    return TimeSpan.Zero;
                }

                var remaining = Timeout - _stopwatch.Elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        public TimeSpan GetRemainingTimeoutOrThrow()
        {
            var remaining = GetRemainingTimeout();
            if (remaining > TimeSpan.Zero)
            {
                return remaining;
            }

            MarkTimedOut();
            throw CreateCompletionTimeoutException();
        }

        public void ThrowIfExpired()
        {
            _ = GetRemainingTimeoutOrThrow();
        }

        public bool TryClaimPublish()
        {
            lock (_lock)
            {
                if (_timedOut || _stopwatch.Elapsed >= Timeout)
                {
                    _timedOut = true;
                    return false;
                }

                _published = true;
                return true;
            }
        }

        public bool TryMarkTimedOut()
        {
            lock (_lock)
            {
                if (_published)
                {
                    return false;
                }

                _timedOut = true;
                return true;
            }
        }

        public TimeoutException CreateCompletionTimeoutException()
            => new($"Timed out after {Timeout.TotalMilliseconds}ms while completing WpfDevTools Inspector SDK initialization on the UI dispatcher.");

        private void MarkTimedOut()
        {
            lock (_lock)
            {
                _timedOut = true;
            }
        }
    }
}
