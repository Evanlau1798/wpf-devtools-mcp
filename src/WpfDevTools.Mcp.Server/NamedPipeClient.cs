using System.Diagnostics;
using System.ComponentModel;
using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Named Pipe client for communicating with Inspector DLL
/// </summary>
internal enum NamedPipeConnectFailure
{
    None = 0,
    Timeout = 1,
    AuthenticationFailed = 2,
    SecureTransportFailed = 3,
    AccessDenied = 4,
    ServerProcessMismatch = 5,
    IncompatibleHost = 6
}

public sealed partial class NamedPipeClient : IDisposable
{
    private const string InspectorCertificateSubject = "CN=WpfDevTools-Inspector";
    private static readonly TimeSpan DisposeInFlightRequestGraceTimeout = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions IpcSerializerOptions = new()
    {
        MaxDepth = 32
    };

    private readonly int _processId;
    private readonly string _pipeName;
    private readonly AuthenticationManager? _authManager;
    private readonly CertificateManager? _certManager;
    private readonly bool _ownsAuthManager;
    private readonly bool _enforceHostCompatibilityValidation;
    private readonly TimeSpan _requestTimeout;
    private NamedPipeClientStream? _pipeClient;
    private Stream? _communicationStream;

    // Lock ordering: _pipeSemaphore is the outer async serialization primitive for connect,
    // request, and dispose paths. _lock protects local pipe state snapshots such as
    // _pipeClient, _communicationStream, and _disposeState.
    // Do not wait on _pipeSemaphore while holding _lock; acquire _pipeSemaphore first,
    // then enter _lock only for short state checks or assignments.
    private readonly object _lock = new();
    private readonly SemaphoreSlim _pipeSemaphore = new(1, 1);
    private int _disposeState;
    private int _lastConnectFailure;
    private static readonly string CurrentBuildFingerprint =
        InspectorCompatibilityContract.GetBuildFingerprint(typeof(NamedPipeClient));

    /// <summary>
    /// Compatibility-only plaintext constructor. Initializes a new instance without authentication or TLS.
    /// </summary>
    /// <remarks>
    /// Production SessionManager paths must use authentication and TLS through the process-scoped
    /// SessionManager factories. This overload exists for legacy compatibility and local tests only.
    /// </remarks>
    /// <param name="processId">Process ID of the target WPF application</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public NamedPipeClient(int processId)
        : this(processId, BuildPipeName(processId), null, null)
    {
    }

    internal NamedPipeClient(int processId, string pipeName)
        : this(processId, pipeName, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the NamedPipeClient class with optional authentication
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    public NamedPipeClient(int processId, AuthenticationManager? authManager)
        : this(processId, BuildPipeName(processId), authManager, null)
    {
    }

    /// <summary>
    /// Initializes a new instance with optional authentication and encryption
    /// </summary>
    /// <param name="processId">Process ID of the target WPF application</param>
    /// <param name="authManager">Authentication manager (null to disable authentication)</param>
    /// <param name="certManager">Certificate manager for SslStream encryption (null to disable encryption)</param>
    public NamedPipeClient(int processId, AuthenticationManager? authManager, CertificateManager? certManager)
        : this(processId, BuildPipeName(processId), authManager, certManager)
    {
    }

    internal NamedPipeClient(
        int processId,
        string pipeName,
        AuthenticationManager? authManager,
        CertificateManager? certManager,
        bool ownsAuthManager = false,
        bool? enforceHostCompatibilityValidation = null,
        TimeSpan? requestTimeout = null)
    {
        _processId = processId;
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? BuildPipeName(processId)
            : pipeName;
        _authManager = authManager;
        _certManager = certManager;
        _ownsAuthManager = ownsAuthManager;
        _requestTimeout = requestTimeout.HasValue && requestTimeout.Value > TimeSpan.Zero
            ? requestTimeout.Value
            : InspectorConfig.RequestTimeout;

        var defaultPipeName = BuildPipeName(processId);
        var usesDefaultPipeName = string.Equals(_pipeName, defaultPipeName, StringComparison.Ordinal) ||
            _pipeName.StartsWith(defaultPipeName + "_", StringComparison.Ordinal);
        _enforceHostCompatibilityValidation =
            enforceHostCompatibilityValidation ?? usesDefaultPipeName;
    }

    private static string BuildPipeName(int processId) => $"WpfDevTools_{processId}";

    /// <summary>
    /// Pipe name
    /// </summary>
    public string PipeName => _pipeName;

    internal NamedPipeConnectFailure LastConnectFailure =>
        (NamedPipeConnectFailure)Volatile.Read(ref _lastConnectFailure);

    /// <summary>
    /// Check if connected
    /// </summary>
    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _pipeClient?.IsConnected ?? false;
            }
        }
    }

    /// <summary>
    /// Connect to Inspector with timeout and retry
    /// </summary>
    /// <param name="timeout">Total timeout budget shared across all retry attempts</param>
    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="cancellationToken">External cancellation token to abort the entire connect operation</param>
    public async Task<bool> ConnectAsync(TimeSpan timeout, int maxRetries = 3, CancellationToken cancellationToken = default)
    {
        await _pipeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetLastConnectFailure(NamedPipeConnectFailure.None);
            var timeoutBudget = Stopwatch.StartNew();

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var remainingTimeout = timeout - timeoutBudget.Elapsed;
                if (remainingTimeout <= TimeSpan.Zero)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
                    return false;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // A reconnect must tear down both the previous pipe and any wrapped SslStream
                    // before publishing a new transport.
                    ResetConnectionState();

                    NamedPipeClientStream localClient;
                    lock (_lock)
                    {
                        if (Volatile.Read(ref _disposeState) != 0)
                            return false;

                        _pipeClient = new NamedPipeClientStream(
                            ".",
                            _pipeName,
                            PipeDirection.InOut,
                            PipeOptions.Asynchronous);
                        localClient = _pipeClient;
                    }

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(remainingTimeout);
                    await WaitForConnectPhaseAsync(localClient.ConnectAsync(cts.Token), cts.Token).ConfigureAwait(false);

                    if (_authManager != null && _authManager.IsAuthenticationEnabled)
                    {
                        if (!await AuthenticateToInspectorAsync(localClient, cts.Token, cancellationToken).ConfigureAwait(false))
                        {
                            ResetConnectionState();
                            return false;
                        }
                    }

                    if (_certManager != null)
                    {
                        var sslStream = await CreateClientSslStreamAsync(localClient, cts.Token, cancellationToken).ConfigureAwait(false);
                        if (sslStream == null)
                        {
                            ResetConnectionState();
                            return false;
                        }

                        lock (_lock)
                        {
                            _communicationStream = sslStream;
                        }
                    }
                    else
                    {
                        lock (_lock)
                        {
                            _communicationStream = localClient;
                        }
                    }

                    if (Volatile.Read(ref _disposeState) != 0)
                    {
                        ResetConnectionState();
                        return false;
                    }

                    if (_enforceHostCompatibilityValidation)
                    {
                        var compatibilityFailure = await ValidateConnectedHostAsync(localClient, cts.Token, cancellationToken).ConfigureAwait(false);
                        if (compatibilityFailure != NamedPipeConnectFailure.None)
                        {
                            SetLastConnectFailure(compatibilityFailure);
                            ResetConnectionState();
                            return false;
                        }
                    }

                    SetLastConnectFailure(NamedPipeConnectFailure.None);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.AccessDenied);
                    if (!await HandleConnectRetryAsync(attempt, maxRetries, timeout, timeoutBudget, cancellationToken).ConfigureAwait(false))
                        return false;
                }
                catch (IOException)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
                    if (!await HandleConnectRetryAsync(attempt, maxRetries, timeout, timeoutBudget, cancellationToken).ConfigureAwait(false))
                        return false;
                }
                catch (TimeoutException)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
                    if (!await HandleConnectRetryAsync(attempt, maxRetries, timeout, timeoutBudget, cancellationToken).ConfigureAwait(false))
                        return false;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ResetConnectionState();
                    throw;
                }
                catch (OperationCanceledException)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
                    if (!await HandleConnectRetryAsync(attempt, maxRetries, timeout, timeoutBudget, cancellationToken).ConfigureAwait(false))
                        return false;
                }
                catch (ObjectDisposedException)
                {
                    ResetConnectionState();
                    return false;
                }
            }

            SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
            return false;
        }
        finally
        {
            _pipeSemaphore.Release();
        }
    }













    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint serverProcessId);




}
