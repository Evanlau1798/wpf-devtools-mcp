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
    private readonly Func<int, string?, TimeSpan, CancellationToken, Task<NamedPipeConnectFailure>> _connectInjectedSessionAsync;
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
            is null
                ? ((processId, pipeName, timeout, cancellationToken) => _sessionManager.ConnectInjectedSessionAsync(processId, pipeName, timeout, cancellationToken))
                : ((processId, _, timeout, cancellationToken) => connectInjectedSessionAsync(processId, timeout, cancellationToken));
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
            var targetValidationFailure = ValidateAndAuthorizeTarget(
                processId,
                cancellationToken,
                out var targetContext);
            if (targetValidationFailure != null)
            {
                return targetValidationFailure;
            }
            var targetContextValue = targetContext!;
            if (!TryRefreshConnectTargetBeforeExistingHostProbe(processId, ref targetContextValue, out var targetRefreshFailure))
            {
                return targetRefreshFailure!;
            }

            var existingHostResult = await ProbeExistingInspectorHostAsync(
                processId,
                targetContextValue,
                connectStopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
            if (existingHostResult != null)
            {
                return existingHostResult;
            }

            if (!targetContextValue.IsRawInjectionTargetAllowed)
            {
                return CreateRawInjectionDeniedFailure(processId, targetContextValue.ProcessInfo);
            }

            if (!TryRefreshRawInjectionTargetBeforeInjection(processId, ref targetContextValue, out targetRefreshFailure))
            {
                return targetRefreshFailure!;
            }

            var injectionPlanFailure = TryCreateInjectionRequest(
                processId,
                targetContextValue,
                connectStopwatch.Elapsed,
                out var injectionRequest);
            if (injectionPlanFailure != null)
            {
                return injectionPlanFailure;
            }

            var injectionFailure = ExecuteBootstrapInjection(
                processId,
                targetContextValue,
                injectionRequest!,
                cancellationToken,
                out var injectedPipeName);
            if (injectionFailure != null)
            {
                return injectionFailure;
            }

            return await ConnectPipeHandshakeAsync(
                processId,
                injectedPipeName,
                connectStopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
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
        bool allowFreshInjectionFallback,
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
        string? existingPipeName = null;
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

            if (_pipeReadyProbe.TryFindReadyPipeByPrefix(
                $"WpfDevTools_{processId}",
                probeTimeout,
                cancellationToken,
                out existingPipeName))
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
                existingPipeName,
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
            if (allowFreshInjectionFallback && IsFreshInjectionRecoverableExistingHostFailure(pipeConnectFailure))
            {
                Trace.TraceWarning(
                    "ConnectTool rejected an existing Inspector host for process {0} with {1}; continuing with fresh injection because raw injection is allowlisted.",
                    processId,
                    pipeConnectFailure);
                return null;
            }

            return CreatePreInjectionConnectFailure(processId, pipeConnectFailure);
        }

        return ConnectOperationResult.ReusedExistingHost;
    }

    private static bool IsFreshInjectionRecoverableExistingHostFailure(NamedPipeConnectFailure failure)
        => failure == NamedPipeConnectFailure.IncompatibleHost;

    private static bool ShouldSkipExistingHostReuseForRawInjection(ConnectTargetContext context)
        => context.IsRawInjectionTargetAllowed
            && !context.LikelySdkOnlyPackaging
            && IsEnvironmentFlagEnabled(Environment.GetEnvironmentVariable(
                McpServerConfiguration.SkipExistingHostReuseEnvVar));

    private static bool IsEnvironmentFlagEnabled(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);










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



}
