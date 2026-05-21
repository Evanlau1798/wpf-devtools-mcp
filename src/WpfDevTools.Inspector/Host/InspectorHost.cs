using System.IO;
using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Hosts the Named Pipe server for Inspector communication
/// </summary>
public sealed partial class InspectorHost : IDisposable
{
    private const string AllowUnsafePlaintextEnvVar = "WPFDEVTOOLS_ALLOW_UNSAFE_PLAINTEXT_INSPECTORHOST";
    private const int LifecycleStopped = 0;
    private const int LifecycleStarting = 1;
    private const int LifecycleRunning = 2;
    private const int LifecycleStopping = 3;

    private readonly int _processId;
    private readonly string _pipeName;
    private readonly FileLogger _logger;
    private readonly RequestDispatcher _dispatcher;
    private readonly AuthenticationManager? _authManager;
    private readonly CertificateManager? _certManager;
    private readonly ChallengeGenerator _challengeGenerator;
    private readonly Func<NamedPipeServerStream>? _pipeServerFactory;
    private readonly Action? _beforeStartupCompletion;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _sessionReadTimeout;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private TaskCompletionSource<object?>? _startupCompletionSource;
    private Task? _serverTask;
    private Task? _stopCompletionTask;
    private volatile bool _isRunning;
    private int _lifecycleState;
    private long _nextServerGeneration;
    private long _activeServerGeneration;
    private long _nextStopOperationId;
    private int _disposeState;
    private readonly object _lock = new object();
    private static readonly AsyncLocal<Func<bool>?> UnsafePlaintextPolicyOverride = new();

    internal static Action ResetMonitoringAction { get; set; } = static () => PerformanceAnalyzer.ResetMonitoring();
    internal static Action StopAllWatchersAction { get; set; } = static () => DependencyPropertyAnalyzer.StopAllWatchers();
    internal static Action UninstallBindingTraceListenerAction { get; set; } = static () => BindingErrorTraceListener.Uninstall();

    /// <summary>
    /// Create a new InspectorHost instance without authentication or encryption (backward compatible)
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    public InspectorHost(int processId)
        : this(processId, CreatePipeName(processId), null, null, FileLogLevel.Warning)
    {
    }

    internal InspectorHost(int processId, string pipeName)
        : this(processId, pipeName, null, null, FileLogLevel.Warning)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with an explicit minimum log level.
    /// </summary>
    public InspectorHost(int processId, FileLogLevel minimumLogLevel)
        : this(processId, CreatePipeName(processId), null, null, minimumLogLevel)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">
    /// Authentication manager (null to disable authentication).
    /// Ownership is transferred to the host and the manager is disposed when the host is disposed.
    /// </param>
    public InspectorHost(int processId, AuthenticationManager? authManager)
        : this(processId, CreatePipeName(processId), authManager, null, FileLogLevel.Warning)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication and encryption
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">
    /// Authentication manager (null to disable authentication).
    /// Ownership is transferred to the host and the manager is disposed when the host is disposed.
    /// </param>
    /// <param name="certManager">Certificate manager for SslStream encryption (null to disable encryption)</param>
    public InspectorHost(int processId, AuthenticationManager? authManager, CertificateManager? certManager)
        : this(processId, CreatePipeName(processId), authManager, certManager, FileLogLevel.Warning)
    {
    }

    internal InspectorHost(
        int processId,
        AuthenticationManager? authManager,
        CertificateManager? certManager,
        TimeSpan startupTimeout)
        : this(
            processId,
            CreatePipeName(processId),
            authManager,
            certManager,
            FileLogLevel.Warning,
            startupTimeout: startupTimeout)
    {
    }

    /// <summary>
    /// Create a new InspectorHost instance with optional authentication, encryption, and explicit log level.
    /// </summary>
    public InspectorHost(int processId, AuthenticationManager? authManager, CertificateManager? certManager, FileLogLevel minimumLogLevel)
        : this(processId, CreatePipeName(processId), authManager, certManager, minimumLogLevel)
    {
    }

    internal InspectorHost(
        int processId,
        string pipeName,
        AuthenticationManager? authManager,
        CertificateManager? certManager,
        FileLogLevel minimumLogLevel = FileLogLevel.Warning,
        Func<NamedPipeServerStream>? pipeServerFactory = null,
        TimeSpan? startupTimeout = null,
        Action? beforeStartupCompletion = null,
        TimeSpan? sessionReadTimeout = null)
    {
        _processId = processId;
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? CreatePipeName(processId)
            : pipeName;
        var logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_Inspector_{processId}.log");
        _logger = new FileLogger(logPath)
        {
            MinimumLevel = minimumLogLevel
        };
        _dispatcher = new RequestDispatcher(_logger, _processId, null);
        _authManager = authManager;
        _certManager = certManager;
        _challengeGenerator = new ChallengeGenerator();
        _pipeServerFactory = pipeServerFactory;
        _startupTimeout = startupTimeout ?? InspectorConfig.PipeConnectTimeout;
        _beforeStartupCompletion = beforeStartupCompletion;
        _sessionReadTimeout = sessionReadTimeout ?? InspectorConfig.IdleConnectionTimeout;
    }

    // SDK-hosted inspectors cannot receive an injection-time randomized pipe handoff.
    // InspectorSdk.Initialize() therefore fails closed unless callers provide matching HMAC/TLS settings.
    private static string CreatePipeName(int processId) => $"WpfDevTools_{processId}";

    /// <summary>
    /// Start the Named Pipe server
    /// </summary>
    /// <remarks>
    /// This method is idempotent - calling it multiple times has no effect if server is already running.
    /// It blocks the calling thread until startup completes or fails, including waiting for asynchronous
    /// server startup work through synchronous waits. It must not be called from a WPF UI thread;
    /// use a background initialization path when hosting the inspector from application code.
    /// </remarks>
    public void Start()
    {
        ThrowIfUnsafePlaintextNotAllowed();

        while (true)
        {
            Task startupTask;
            Task startupReadinessTask = Task.CompletedTask;
            Task? serverTask = null;
            Task? stopTask = null;
            CancellationTokenSource? cancellationTokenSource = null;
            TaskCompletionSource<object?>? startupCompletionSource = null;
            TaskCompletionSource<object?>? startupReadinessCompletionSource = null;
            long generation;
            var ownsStartup = false;

            lock (_lock)
            {
                if (_disposeState != 0)
                {
                    throw new ObjectDisposedException(nameof(InspectorHost));
                }

                if (_stopCompletionTask != null)
                {
                    stopTask = _stopCompletionTask;
                    generation = _activeServerGeneration;
                    startupTask = Task.CompletedTask;
                }
                else
                {
                    if (_lifecycleState == LifecycleRunning)
                    {
                        return;
                    }

                    if (_lifecycleState == LifecycleStarting)
                    {
                        generation = _activeServerGeneration;
                        startupTask = _startupCompletionSource!.Task;
                    }
                    else if (_lifecycleState == LifecycleStopping)
                    {
                        stopTask = _serverTask;
                        generation = _activeServerGeneration;
                        startupTask = Task.CompletedTask;
                    }
                    else
                    {
                        ownsStartup = true;
                        _lifecycleState = LifecycleStarting;
                        generation = ++_nextServerGeneration;
                        _activeServerGeneration = generation;
                        _cancellationTokenSource = new CancellationTokenSource();
                        cancellationTokenSource = _cancellationTokenSource;
                        _startupCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        startupCompletionSource = _startupCompletionSource;
                        startupTask = _startupCompletionSource.Task;
                        startupReadinessCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        startupReadinessTask = startupReadinessCompletionSource.Task;
                        var startupCancellationToken = _cancellationTokenSource.Token;
                        _serverTask = Task.Factory
                            .StartNew(
                                () => RunServerLoop(startupCancellationToken, startupReadinessCompletionSource, generation),
                                CancellationToken.None,
                                TaskCreationOptions.LongRunning,
                                TaskScheduler.Default)
                            .Unwrap();
                        serverTask = _serverTask;
                    }
                }
            }

            if (stopTask != null)
            {
                WaitForStopCompletion(stopTask);
                continue;
            }

            if (ownsStartup)
            {
                try
                {
                    WaitForStartup(startupReadinessTask, serverTask!);
                    _beforeStartupCompletion?.Invoke();
                    CompleteStartupSuccess(generation, startupTask);
                    EnsureStartupReachedRunning(generation);
                }
                catch (Exception ex)
                {
                    CompleteStartupFailure(generation, cancellationTokenSource!, startupCompletionSource!, serverTask!, ex);
                    ExceptionDispatchInfo.Capture(ex).Throw();
                }

                return;
            }

            startupTask.GetAwaiter().GetResult();
            CompleteStartupSuccess(generation, startupTask);
            EnsureStartupReachedRunning(generation);
            return;
        }
    }







    private static readonly JsonSerializerOptions IpcSerializerOptions = new()
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = true
    };







    /// <summary>
    /// Gets whether the Inspector server is currently running
    /// </summary>
    public bool IsRunning => _isRunning;

    internal bool IsDisposed => System.Threading.Volatile.Read(ref _disposeState) == 2;

    internal static IDisposable BeginUnsafePlaintextPolicyTestScope(Func<bool> policy)
    {
        var previous = UnsafePlaintextPolicyOverride.Value;
        UnsafePlaintextPolicyOverride.Value = policy;
        return new TestPolicyScope(() => UnsafePlaintextPolicyOverride.Value = previous);
    }

    private void ThrowIfUnsafePlaintextNotAllowed()
    {
        var hasAuthentication = _authManager?.IsAuthenticationEnabled == true;
        var hasTls = _certManager != null;
        if ((hasAuthentication && hasTls) || IsUnsafePlaintextAllowed())
        {
            return;
        }

        throw new InvalidOperationException(
            "Starting an unsafe plaintext or partially secured InspectorHost requires explicit opt-in. " +
            $"Set {AllowUnsafePlaintextEnvVar}=1 only for local tests or use InspectorSdk.Initialize with authentication and TLS.");
    }

    private static bool IsUnsafePlaintextAllowed()
    {
        if (UnsafePlaintextPolicyOverride.Value is { } overridePolicy)
        {
            return overridePolicy();
        }

        var value = Environment.GetEnvironmentVariable(AllowUnsafePlaintextEnvVar);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestPolicyScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

}

