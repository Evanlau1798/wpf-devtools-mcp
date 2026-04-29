using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to connect to a WPF process.
///
/// This class intentionally does NOT extend PipeConnectedToolBase because the connect
/// tool is responsible for establishing the Named Pipe connection in the first place.
/// PipeConnectedToolBase requires an existing pipe session to send inspector requests,
/// but ConnectTool must inject the Inspector DLL and create the session before any pipe
/// communication is possible.
/// </summary>
public sealed partial class ConnectTool
{
    private static readonly ConcurrentDictionary<ConnectOperationKey, InflightConnectOperation> GlobalInflightConnects = new();
    private const string InjectionBudgetExhaustedMessage = "Injection timed out before the bootstrap phase could start.";
    private const string PipeReadyBudgetExhaustedMessage = "Bootstrap completed, but the remaining connect budget was exhausted before the Inspector Named Pipe readiness check could start.";

    private readonly IProcessInjector _injector;
    private readonly SessionManager _sessionManager;
    private readonly WpfProcessDetector _processDetector;
    private readonly Action<string> _dllPathValidator;
    private readonly Func<bool> _isCurrentProcessElevated;
    private readonly Func<int, long> _workingSetResolver;
    private readonly Func<string, IEnumerable<string>> _inspectorCandidateResolver;
    private readonly Func<string, IEnumerable<string>> _bootstrapperCandidateResolver;
    private readonly PipeReadyProbe _pipeReadyProbe;
    private readonly Func<WpfProcessInfo, bool> _isRawInjectionTargetAllowed;
    private readonly Func<WpfProcessInfo, McpTargetAuthorization> _targetPolicy;
    private readonly Func<int, TimeSpan, CancellationToken, Task<NamedPipeConnectFailure>> _connectInjectedSessionAsync;
    private readonly TimeSpan _connectTimeout;

    /// <summary>
    /// Create ConnectTool with dependency injection
    /// </summary>
    public ConnectTool(
        SessionManager sessionManager,
        IProcessInjector injector,
        WpfProcessDetector? processDetector = null,
        Action<string>? dllPathValidator = null,
        Func<bool>? isCurrentProcessElevated = null,
        Func<int, long>? workingSetResolver = null,
        Func<string, IEnumerable<string>>? inspectorCandidateResolver = null,
        Func<string, IEnumerable<string>>? bootstrapperCandidateResolver = null,
        PipeReadyProbe? pipeReadyProbe = null,
        Func<WpfProcessInfo, bool>? isRawInjectionTargetAllowed = null,
        Func<WpfProcessInfo, McpTargetAuthorization>? targetPolicy = null)
        : this(
            sessionManager,
            injector,
            processDetector,
            dllPathValidator,
            isCurrentProcessElevated,
            workingSetResolver,
            inspectorCandidateResolver,
            bootstrapperCandidateResolver,
            pipeReadyProbe,
            isRawInjectionTargetAllowed,
            targetPolicy,
                connectInjectedSessionAsync: null,
                connectTimeout: null)
    {
    }

    internal ConnectTool(
        SessionManager sessionManager,
        IProcessInjector injector,
        WpfProcessDetector? processDetector,
        Action<string>? dllPathValidator,
        Func<bool>? isCurrentProcessElevated,
        Func<int, long>? workingSetResolver,
        Func<string, IEnumerable<string>>? inspectorCandidateResolver,
        Func<string, IEnumerable<string>>? bootstrapperCandidateResolver,
        PipeReadyProbe? pipeReadyProbe,
        Func<WpfProcessInfo, bool>? isRawInjectionTargetAllowed,
        Func<WpfProcessInfo, McpTargetAuthorization>? targetPolicy,
        Func<int, TimeSpan, CancellationToken, Task<NamedPipeConnectFailure>>? connectInjectedSessionAsync,
        TimeSpan? connectTimeout)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _processDetector = processDetector ?? new WpfProcessDetector();
        _dllPathValidator = dllPathValidator ?? DllPathValidator.ValidateDllPath;
        _isCurrentProcessElevated = isCurrentProcessElevated ?? CurrentProcessElevationDetector.IsCurrentProcessElevated;
        _workingSetResolver = workingSetResolver ?? ResolveWorkingSetBytes;
        _inspectorCandidateResolver = inspectorCandidateResolver ?? DllCandidateResolver.EnumerateInspectorCandidates;
        _bootstrapperCandidateResolver = bootstrapperCandidateResolver ?? DllCandidateResolver.EnumerateBootstrapperCandidates;
        _pipeReadyProbe = pipeReadyProbe ?? new PipeReadyProbe();
        _isRawInjectionTargetAllowed = isRawInjectionTargetAllowed ?? RawInjectionTargetPolicy.IsAllowed;
        _targetPolicy = targetPolicy ?? McpTargetPolicy.Authorize;
        _connectInjectedSessionAsync = connectInjectedSessionAsync
            ?? ((processId, timeout, cancellationToken) => _sessionManager.ConnectInjectedSessionAsync(processId, timeout, cancellationToken));
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(McpServerConfiguration.ConnectTimeoutSeconds);
    }

    /// <summary>
    /// Create ConnectTool (backward compatibility constructor)
    /// </summary>
    public ConnectTool(SessionManager sessionManager)
        : this(sessionManager, new ProcessInjector())
    {
    }

    /// <summary>
    /// Execute the tool to connect to a WPF process
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        int? processId = null;
        var explicitProcessSelection = false;
        if (arguments.HasValue && arguments.Value.TryGetProperty("processId", out var pidProp))
        {
            if (!pidProp.TryGetInt32(out var parsedPid))
            {
                return new
                {
                    success = false,
                    error = "processId must be a valid 32-bit integer",
                    errorCode = "InvalidArgument"
                };
            }

            processId = parsedPid;
            explicitProcessSelection = true;
        }

        var selectionStrategyError = TryGetOptionalString(arguments, "selectionStrategy", out var selectionStrategyValue);
        if (selectionStrategyError != null)
        {
            return selectionStrategyError;
        }

        if (!ProcessDiscoverySelectionStrategies.TryParse(selectionStrategyValue, out var selectionStrategy))
        {
            return new
            {
                success = false,
                error = "selectionStrategy must be 'single_only' or 'largest_working_set'",
                errorCode = "InvalidArgument",
                hint = "Omit selectionStrategy for the safe default, or use largest_working_set only when automatic disambiguation is acceptable."
            };
        }

        var windowFilterError = TryGetOptionalString(arguments, "windowFilter", out var windowFilterValue);
        if (windowFilterError != null)
        {
            return windowFilterError;
        }

        if (!ProcessWindowFilters.TryParse(windowFilterValue, out var windowFilter))
        {
            return new
            {
                success = false,
                error = "windowFilter must be 'all', 'visible', or 'foreground'",
                errorCode = "InvalidArgument",
                hint = "Omit windowFilter for the visible-only default, or use all to include background WPF windows during auto-discovery."
            };
        }

        AutoDiscoveryResolution? autoDiscoveryResolution = null;
        if (!processId.HasValue)
        {
            autoDiscoveryResolution = TryResolveAutoDiscoveredProcess(selectionStrategy, windowFilter);
            if (autoDiscoveryResolution.ErrorResult != null)
            {
                return autoDiscoveryResolution.ErrorResult;
            }

            processId = autoDiscoveryResolution.ProcessId;
        }

        if (!processId.HasValue)
        {
            return new
            {
                success = false,
                error = "Missing required parameter: processId",
                errorCode = "InvalidArgument",
                hint = "Call connect() with no processId only when auto-discovery can resolve a single WPF target."
            };
        }

        if (processId.Value <= 0)
        {
            return new
            {
                success = false,
                error = "processId must be a positive integer",
                errorCode = "InvalidArgument"
            };
        }

        var connectResult = await RunSingleFlightAsync(
            processId.Value,
            cancellationToken,
            sharedCancellationToken => ExecuteForProcessAsync(
                processId.Value,
                sharedCancellationToken)).ConfigureAwait(false);

        return CreateConnectResponse(
            connectResult,
            processId.Value,
            explicitProcessSelection,
            selectionStrategy,
            autoDiscoveryResolution);
    }

    private async Task<object> ExecuteForProcessAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        var connectStopwatch = Stopwatch.StartNew();
        try
        {
            if (_sessionManager.HasSession(processId))
            {
                if (_sessionManager.TryActivateConnectedSession(processId))
                {
                    return ConnectOperationResult.AlreadyConnected;
                }

                _sessionManager.RemoveSession(processId);
            }

            var rateLimitStatus = _sessionManager.CheckRateLimitStatus(processId);
            if (!rateLimitStatus.Allowed)
            {
                return RateLimitResponseFactory.Create(
                    rateLimitStatus,
                    "Rate limit exceeded for connect operations. Please slow down your requests.");
            }

            var processInfo = _processDetector.GetProcessInfo(processId);
            if (processInfo == null)
            {
                return new
                {
                    success = false,
                    error = $"Could not detect process info for {processId}",
                    errorCode = "ProcessNotFound"
                };
            }

            var targetAuthorization = _targetPolicy(processInfo);
            if (!targetAuthorization.IsAllowed)
            {
                Trace.WriteLine($"ConnectTool target allowlist denied process {processId}: executable={processInfo.ExecutablePath}");
                return new
                {
                    success = false,
                    error = targetAuthorization.Error,
                    errorCode = "SecurityError",
                    hint = targetAuthorization.Hint,
                    requiresExplicitTargetOptIn = true,
                    policyEnvVar = McpServerConfiguration.AllowedTargetsEnvVar,
                    targetProcessName = processInfo.ProcessName
                };
            }

            var access = ProcessConnectionAccessEvaluator.Evaluate(
                processId,
                processInfo.IsElevated,
                _isCurrentProcessElevated());
            if (access.RequiresElevationToConnect)
            {
                return new
                {
                    success = false,
                    error = access.ConnectionWarning,
                    errorCode = InjectionError.AccessDenied.ToString(),
                    targetIsElevated = processInfo.IsElevated,
                    requiresElevationToConnect = access.RequiresElevationToConnect,
                    canConnectFromCurrentServer = access.CanConnectFromCurrentServer,
                    suggestedAction = "Restart the MCP server as administrator and retry connect."
                };
            }

            try
            {
                _sessionManager.EnsureSecureTransportArtifactsCreated();
            }
            catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested && IsSessionManagerDisposed(ex))
            {
                return CreateSessionManagerDisposedFailure();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not ObjectDisposedException)
            {
                Trace.WriteLine($"ConnectTool secure transport initialization failed for process {processId}: {ex}");
                return new
                {
                    success = false,
                    error = "Failed to prepare secure transport artifacts. Check server logs for details.",
                    errorCode = "SecureTransportInitializationFailed",
                    targetIsElevated = processInfo.IsElevated,
                    requiresElevationToConnect = access.RequiresElevationToConnect,
                    canConnectFromCurrentServer = access.CanConnectFromCurrentServer
                };
            }

            var likelySdkOnlyPackaging = IsLikelySdkOnlyPackaging(processInfo);
            var isRawInjectionTargetAllowed = _isRawInjectionTargetAllowed(processInfo);
            var existingHostProbeBudget = likelySdkOnlyPackaging
                ? (isRawInjectionTargetAllowed
                    ? _connectTimeout
                    : McpServerConfiguration.ExternalSdkHostReuseGracePeriod)
                : TimeSpan.FromMilliseconds(250);

            var preInjectionConnectAttempt = await TryConnectToExistingInspectorHostAsync(
                processId,
                likelySdkOnlyPackaging,
                connectStopwatch.Elapsed,
                existingHostProbeBudget,
                cancellationToken).ConfigureAwait(false);
            if (preInjectionConnectAttempt is object preExistingHostResult)
            {
                return preExistingHostResult;
            }

            if (!isRawInjectionTargetAllowed)
            {
                var authorization = RawInjectionTargetPolicy.Authorize(processInfo);
                Trace.WriteLine($"ConnectTool raw injection denied process {processId}: executable={processInfo.ExecutablePath}");
                return new
                {
                    success = false,
                    error = authorization.Error,
                    errorCode = "SecurityError",
                    hint = authorization.Hint,
                    requiresExplicitTargetOptIn = true,
                    allowlistEnvVar = McpServerConfiguration.RawInjectionAllowedTargetsEnvVar,
                    targetProcessName = processInfo.ProcessName
                };
            }

            var validationError = likelySdkOnlyPackaging
                ? InjectionError.SingleFileApplication
                : _injector.ValidateTarget(processId);

            if (validationError != InjectionError.None)
            {
                return new
                {
                    success = false,
                    error = GetErrorMessage(validationError, processId, processInfo),
                    errorCode = validationError.ToString(),
                    targetIsElevated = processInfo.IsElevated,
                    requiresElevationToConnect = access.RequiresElevationToConnect,
                    canConnectFromCurrentServer = access.CanConnectFromCurrentServer
                };
            }

            var inspectorCandidates = _inspectorCandidateResolver(AppContext.BaseDirectory)
                .Where(File.Exists)
                .ToArray();
            var bootstrapperCandidates = _bootstrapperCandidateResolver(AppContext.BaseDirectory)
                .Where(File.Exists)
                .ToArray();

            var injectionRequest = InjectionPlanFactory.CreateRequest(
                processInfo,
                inspectorCandidates,
                bootstrapperCandidates,
                _sessionManager.GetAuthenticationSecretBase64(processId),
                _sessionManager.GetCertificateDirectory());

            if (injectionRequest == null)
            {
                return new
                {
                    success = false,
                    error = "No matching Inspector or Bootstrapper DLL found for target process. " +
                        $"Runtime: {processInfo.Runtime}, Architecture: {processInfo.Architecture}",
                    errorCode = "FileNotFound",
                    hint = "Verify that the server was built for the correct architecture and that Inspector/Bootstrapper DLLs exist in the output directory."
                };
            }

            _dllPathValidator(injectionRequest.InspectorDllPath);
            _dllPathValidator(injectionRequest.BootstrapperDllPath);

            var remainingConnectTimeoutBeforeInjection = GetRemainingPipeConnectTimeout(
                connectStopwatch.Elapsed,
                _connectTimeout);
            if (remainingConnectTimeoutBeforeInjection <= TimeSpan.Zero)
            {
                return new
                {
                    success = false,
                    error = "Connect timed out before bootstrap injection could start.",
                    errorCode = "Timeout",
                    hint = "The pre-injection discovery and validation phases consumed the full connect budget. Retry connect or target the process explicitly."
                };
            }

            injectionRequest = injectionRequest.WithTotalTimeout(remainingConnectTimeoutBeforeInjection);

            InjectionResult injectionResult;
            try
            {
                injectionResult = _sessionManager.ExecuteWithShutdownGuard(
                    () => _injector.InjectWithBootstrap(injectionRequest, cancellationToken));
            }
            catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested && IsSessionManagerDisposed(ex))
            {
                return CreateSessionManagerDisposedFailure();
            }

            if (!injectionResult.Success)
            {
                if (injectionResult.Error == InjectionError.AccessDenied && processInfo.IsElevated)
                {
                    return new
                    {
                        success = false,
                        error = GetErrorMessage(InjectionError.AccessDenied, processId, processInfo),
                        errorCode = InjectionError.AccessDenied.ToString(),
                        targetIsElevated = processInfo.IsElevated,
                        requiresElevationToConnect = access.RequiresElevationToConnect,
                        canConnectFromCurrentServer = access.CanConnectFromCurrentServer,
                        stage = injectionResult.FailedAtStage?.ToString()
                    };
                }

                var injectionFailure = DescribeInjectionFailure(injectionResult, processId, processInfo);

                return new
                {
                    success = false,
                    error = injectionFailure.Error,
                    errorCode = injectionFailure.ErrorCode,
                    stage = injectionResult.FailedAtStage?.ToString()
                };
            }

            try
            {
                var remainingPipeConnectTimeout = GetRemainingPipeConnectTimeout(
                    connectStopwatch.Elapsed,
                    _connectTimeout);
                if (remainingPipeConnectTimeout <= TimeSpan.Zero)
                {
                    return new
                    {
                        success = false,
                        error = "Connect timed out before the final Inspector Named Pipe handshake could start",
                        errorCode = "Timeout",
                        hint = "The injection phase consumed the full timeout budget. Target process may be slow to load the Inspector DLL."
                    };
                }

                NamedPipeConnectFailure pipeConnectFailure;
                try
                {
                    pipeConnectFailure = await _connectInjectedSessionAsync(
                        processId,
                        remainingPipeConnectTimeout,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested && IsSessionManagerDisposed(ex))
                {
                    return CreateSessionManagerDisposedFailure();
                }
                catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    return CreateSessionConnectionFailure(processId, ex);
                }

                if (pipeConnectFailure != NamedPipeConnectFailure.None)
                {
                    var describedFailure = DescribePipeConnectFailure(pipeConnectFailure, processId);
                    return new
                    {
                        success = false,
                        error = describedFailure.Error,
                        errorCode = describedFailure.ErrorCode,
                        hint = describedFailure.Hint
                    };
                }

                return ConnectOperationResult.FreshConnect;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "ConnectTool cleanup triggered for process {0} after pipe handshake failure: {1}: {2}",
                    processId,
                    ex.GetType().Name,
                    ex.Message);
                throw;
            }
        }
        catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested && IsSessionManagerDisposed(ex))
        {
            return CreateSessionManagerDisposedFailure();
        }
    }

    private static bool IsSessionManagerDisposed(ObjectDisposedException exception)
    {
        return string.Equals(exception.ObjectName, nameof(SessionManager), StringComparison.Ordinal);
    }

    private async Task<object?> TryConnectToExistingInspectorHostAsync(
        int processId,
        bool preferRootAuthentication,
        TimeSpan elapsedBeforeProbe,
        TimeSpan probeBudget,
        CancellationToken cancellationToken)
    {
        var remainingPipeConnectTimeout = GetRemainingPipeConnectTimeout(
            elapsedBeforeProbe,
            _connectTimeout);
        if (remainingPipeConnectTimeout <= TimeSpan.Zero)
        {
            return null;
        }

        var effectiveProbeBudget = probeBudget < remainingPipeConnectTimeout
            ? probeBudget
            : remainingPipeConnectTimeout;

        var probeStopwatch = Stopwatch.StartNew();
        while (true)
        {
            var remainingProbeBudget = effectiveProbeBudget - probeStopwatch.Elapsed;
            if (remainingProbeBudget <= TimeSpan.Zero)
            {
                return null;
            }

            var probeTimeout = remainingProbeBudget > TimeSpan.FromMilliseconds(250)
                ? TimeSpan.FromMilliseconds(250)
                : remainingProbeBudget;

            if (_pipeReadyProbe.WaitForPipeReady($"WpfDevTools_{processId}", probeTimeout, cancellationToken))
            {
                break;
            }
        }

        remainingPipeConnectTimeout -= probeStopwatch.Elapsed;
        if (remainingPipeConnectTimeout <= TimeSpan.Zero)
        {
            return CreatePreInjectionConnectFailure(processId, NamedPipeConnectFailure.Timeout);
        }

        NamedPipeConnectFailure pipeConnectFailure;
        try
        {
            pipeConnectFailure = await _sessionManager.ConnectExistingHostSessionAsync(
                processId,
                remainingPipeConnectTimeout,
                cancellationToken,
                preferRootAuthentication).ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested && IsSessionManagerDisposed(ex))
        {
            return CreateSessionManagerDisposedFailure();
        }
        catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateSessionConnectionFailure(processId, ex);
        }

        if (pipeConnectFailure != NamedPipeConnectFailure.None)
        {
            return CreatePreInjectionConnectFailure(processId, pipeConnectFailure);
        }

        return ConnectOperationResult.ReusedExistingHost;
    }

    private static object CreateConnectResponse(
        object connectResult,
        int processId,
        bool explicitProcessSelection,
        ProcessDiscoverySelectionStrategy selectionStrategy,
        AutoDiscoveryResolution? autoDiscoveryResolution)
    {
        if (connectResult is not ConnectOperationResult result)
        {
            return connectResult;
        }

        return result.SuccessKind switch
        {
            ConnectSuccessKind.AlreadyConnected => CreateAlreadyConnectedResponse(
                processId,
                explicitProcessSelection,
                selectionStrategy,
                autoDiscoveryResolution),
            ConnectSuccessKind.ReusedExistingHost => CreateSuccessfulConnectResponse(
                processId,
                explicitProcessSelection,
                selectionStrategy,
                autoDiscoveryResolution,
                reusedExistingHost: true),
            _ => CreateSuccessfulConnectResponse(
                processId,
                explicitProcessSelection,
                selectionStrategy,
                autoDiscoveryResolution,
                reusedExistingHost: false)
        };
    }

    private static object CreateAlreadyConnectedResponse(
        int processId,
        bool explicitProcessSelection,
        ProcessDiscoverySelectionStrategy selectionStrategy,
        AutoDiscoveryResolution? autoDiscoveryResolution)
    {
        var message = BuildConnectSuccessMessage("Already connected to process.");

        if (!explicitProcessSelection &&
            autoDiscoveryResolution?.SelectedCandidate is ProcessDiscoveryCandidateSummary candidate)
        {
            return new
            {
                success = true,
                message,
                processId,
                processName = candidate.ProcessName,
                windowTitle = candidate.WindowTitle,
                autoDiscovered = true,
                autoSelected = autoDiscoveryResolution.AutoSelected,
                selectionReason = autoDiscoveryResolution.AutoSelected
                    ? ProcessDiscoverySelectionStrategies.ToContractValue(selectionStrategy)
                    : null,
                candidateCount = autoDiscoveryResolution.CandidateCount,
                processes = autoDiscoveryResolution.Candidates.Select(ToContractCandidate).ToArray()
            };
        }

        return new
        {
            success = true,
            message,
            processId
        };
    }

    private static object CreateSuccessfulConnectResponse(
        int processId,
        bool explicitProcessSelection,
        ProcessDiscoverySelectionStrategy selectionStrategy,
        AutoDiscoveryResolution? autoDiscoveryResolution,
        bool reusedExistingHost)
    {
        var message = reusedExistingHost
            ? BuildConnectSuccessMessage("Connected to an existing Inspector host.")
            : BuildConnectSuccessMessage("Connected successfully.");

        if (!explicitProcessSelection &&
            autoDiscoveryResolution?.SelectedCandidate is ProcessDiscoveryCandidateSummary candidate)
        {
            if (reusedExistingHost)
            {
                return new
                {
                    success = true,
                    message,
                    processId,
                    processName = candidate.ProcessName,
                    windowTitle = candidate.WindowTitle,
                    autoDiscovered = true,
                    autoSelected = autoDiscoveryResolution.AutoSelected,
                    selectionReason = autoDiscoveryResolution.AutoSelected
                        ? ProcessDiscoverySelectionStrategies.ToContractValue(selectionStrategy)
                        : null,
                    candidateCount = autoDiscoveryResolution.CandidateCount,
                    processes = autoDiscoveryResolution.Candidates.Select(ToContractCandidate).ToArray(),
                    reusedExistingHost = true
                };
            }

            return new
            {
                success = true,
                message,
                processId,
                processName = candidate.ProcessName,
                windowTitle = candidate.WindowTitle,
                autoDiscovered = true,
                autoSelected = autoDiscoveryResolution.AutoSelected,
                selectionReason = autoDiscoveryResolution.AutoSelected
                    ? ProcessDiscoverySelectionStrategies.ToContractValue(selectionStrategy)
                    : null,
                candidateCount = autoDiscoveryResolution.CandidateCount,
                processes = autoDiscoveryResolution.Candidates.Select(ToContractCandidate).ToArray()
            };
        }

        if (reusedExistingHost)
        {
            return new
            {
                success = true,
                message,
                processId,
                reusedExistingHost = true
            };
        }

        return new
        {
            success = true,
            message,
            processId
        };
    }

    private static string BuildConnectSuccessMessage(string prefix)
        => $"{prefix} Start with get_ui_summary, get_element_snapshot, or get_form_summary to build scene-first context before any tree-heavy follow-up.";

    private static object CreatePreInjectionConnectFailure(
        int processId,
        NamedPipeConnectFailure failure)
    {
        var pipeConnectFailure = DescribePipeConnectFailure(failure, processId);
        return new
        {
            success = false,
            error = pipeConnectFailure.Error,
            errorCode = pipeConnectFailure.ErrorCode,
            hint = pipeConnectFailure.Hint
        };
    }

    private object CreateSessionConnectionFailure(int processId, InvalidOperationException exception)
    {
        if (_sessionManager.TryActivateConnectedSession(processId))
        {
            return ConnectOperationResult.AlreadyConnected;
        }

        if (exception.Message.Contains("Maximum session limit", StringComparison.Ordinal))
        {
            Trace.WriteLine($"ConnectTool session limit prevented attach for process {processId}: {exception}");
            return new
            {
                success = false,
                error = "The MCP server has reached its maximum number of active sessions.",
                errorCode = "SessionLimitExceeded",
                hint = "Disconnect an existing session before connecting another target process."
            };
        }

        if (exception.Message.Contains("already exists", StringComparison.Ordinal))
        {
            return new
            {
                success = false,
                error = $"A session for process {processId} was created concurrently before connect could finish attaching.",
                errorCode = "SessionConflict",
                hint = "Retry connect, or reuse the existing session if it is already connected."
            };
        }

        Trace.WriteLine($"ConnectTool session connection failure for process {processId}: {exception}");

        return new
        {
            success = false,
            error = "Connect could not attach the new session because the server encountered an internal error.",
            errorCode = "InternalError",
            hint = "Retry connect. If the problem persists, restart the MCP server."
        };
    }

    private static (string ErrorCode, string Error) DescribeInjectionFailure(
        InjectionResult injectionResult,
        int processId,
        WpfProcessInfo processInfo)
    {
        var errorCode = injectionResult.Error switch
        {
            InjectionError.Timeout or InjectionError.PipeReadyTimeout => "Timeout",
            _ => injectionResult.Error.ToString()
        };

        var error = injectionResult.Error switch
        {
            InjectionError.ProcessNotFound or
            InjectionError.NotWpfApplication or
            InjectionError.ArchitectureMismatch or
            InjectionError.SingleFileApplication => GetErrorMessage(injectionResult.Error, processId, processInfo),
            InjectionError.AccessDenied => "Access denied while starting the injected inspector.",
            InjectionError.AllocationFailed => "Failed to allocate remote memory during injection.",
            InjectionError.WriteFailed => "Failed to write injector payload into the target process.",
            InjectionError.CreateThreadFailed => "Failed to start the remote injection thread.",
            InjectionError.Timeout when injectionResult.TimeoutReason == InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart
                => InjectionBudgetExhaustedMessage,
            InjectionError.Timeout => "Injection timed out before the target process became ready.",
            InjectionError.PipeReadyTimeout when injectionResult.TimeoutReason == InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart
                => PipeReadyBudgetExhaustedMessage,
            InjectionError.PipeReadyTimeout => "Bootstrap completed, but the Inspector Named Pipe did not become ready before the timeout expired.",
            InjectionError.BootstrapFailed => DescribeBootstrapFailureMessage(injectionResult),
            InjectionError.Unknown => "Injection failed due to an unexpected internal error. Check server logs for details.",
            _ => "Injection failed"
        };

        Trace.WriteLine(
            $"ConnectTool injection failure for process {processId}: error={injectionResult.Error}; " +
            $"stage={injectionResult.FailedAtStage}; exitCode={injectionResult.BootstrapExitCode}; " +
            $"detail={injectionResult.ErrorMessage}");

        return (errorCode, error);
    }

    private static string DescribeBootstrapFailureMessage(InjectionResult injectionResult)
    {
        if (injectionResult.BootstrapExitCode is int exitCode &&
            exitCode < 0)
        {
            return "Bootstrap failed while starting the injected inspector.";
        }

        return injectionResult.FailedAtStage switch
        {
            BootstrapStage.ClrDetection => "Bootstrap failed during CLR detection.",
            BootstrapStage.ClrHosting => "Bootstrap failed during CLR hosting initialization.",
            BootstrapStage.ManagedEntrypoint => "Bootstrap failed while invoking the managed bootstrap entrypoint.",
            BootstrapStage.LoadLibrary => "Bootstrap failed while loading the inspector DLL.",
            BootstrapStage.PipeReady => "Bootstrap completed, but the Inspector Named Pipe did not become ready before the timeout expired.",
            _ => "Bootstrap failed while starting the injected inspector."
        };
    }

    private static object CreateSessionManagerDisposedFailure()
    {
        return new
        {
            success = false,
            error = "Connect cannot continue because the session manager is shutting down.",
            errorCode = "ServerShuttingDown",
            hint = "Wait for the MCP server to finish shutting down or restart it before retrying connect."
        };
    }

    private sealed record ConnectOperationResult(ConnectSuccessKind SuccessKind)
    {
        public static readonly ConnectOperationResult AlreadyConnected = new(ConnectSuccessKind.AlreadyConnected);
        public static readonly ConnectOperationResult FreshConnect = new(ConnectSuccessKind.FreshConnect);
        public static readonly ConnectOperationResult ReusedExistingHost = new(ConnectSuccessKind.ReusedExistingHost);
    }

    private enum ConnectSuccessKind
    {
        AlreadyConnected,
        FreshConnect,
        ReusedExistingHost
    }

    internal static (string ErrorCode, string Error, string Hint) DescribePipeConnectFailure(
        NamedPipeConnectFailure failure,
        int processId)
    {
        return failure switch
        {
            NamedPipeConnectFailure.AuthenticationFailed => (
                "SecurityError",
                $"Authentication failed connecting to Inspector Named Pipe for process {processId}.",
                "The shared secret used by the MCP server does not match the injected Inspector. Restart the target or reconnect after refreshing the secure session state."),
            NamedPipeConnectFailure.SecureTransportFailed => (
                "SecurityError",
                $"Secure transport handshake failed connecting to Inspector Named Pipe for process {processId}.",
                "Verify certificate or thumbprint configuration, then retry connect. A stale or mismatched certificate directory can cause this failure."),
            NamedPipeConnectFailure.ServerProcessMismatch => (
                "SecurityError",
                $"Connected Named Pipe server for process {processId} is not hosted by the requested target process.",
                "A different local process is advertising the expected pipe name. Restart the target process, then retry connect so the MCP server can attach to the real Inspector host."),
            NamedPipeConnectFailure.IncompatibleHost => (
                "CompatibilityError",
                $"Existing Inspector host for process {processId} is incompatible with the current MCP server build.",
                "Restart the target process so connect() can inject or reuse an Inspector host that matches the current protocol and build."),
            NamedPipeConnectFailure.AccessDenied => (
                "AccessDenied",
                $"Access denied connecting to Inspector Named Pipe for process {processId}.",
                "Ensure the MCP server and target process run under compatible privileges and user sessions before retrying connect."),
            _ => (
                "Timeout",
                "Timeout connecting to Inspector Named Pipe",
                "The Inspector DLL may not have started its Named Pipe server. Check if the target process is frozen or if antivirus is blocking the pipe.")
        };
    }

    private static object? TryGetOptionalString(
        JsonElement? arguments,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!arguments.HasValue || !arguments.Value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return new
            {
                success = false,
                error = $"{propertyName} must be a string when provided",
                errorCode = "InvalidArgument",
                hint = $"Provide {propertyName} as a JSON string value."
            };
        }

        value = property.GetString();
        return null;
    }

}
