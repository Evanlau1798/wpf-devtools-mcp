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
    private readonly IProcessInjector _injector;
    private readonly SessionManager _sessionManager;
    private readonly WpfProcessDetector _processDetector;
    private readonly Action<string> _dllPathValidator;
    private readonly Func<bool> _isCurrentProcessElevated;
    private readonly Func<int, long> _workingSetResolver;
    private readonly Func<string, IEnumerable<string>> _inspectorCandidateResolver;
    private readonly Func<string, IEnumerable<string>> _bootstrapperCandidateResolver;
    private readonly ConcurrentDictionary<int, InflightConnectOperation> _inflightConnects = new();

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
        Func<string, IEnumerable<string>>? bootstrapperCandidateResolver = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _processDetector = processDetector ?? new WpfProcessDetector();
        _dllPathValidator = dllPathValidator ?? DllPathValidator.ValidateDllPath;
        _isCurrentProcessElevated = isCurrentProcessElevated ?? CurrentProcessElevationDetector.IsCurrentProcessElevated;
        _workingSetResolver = workingSetResolver ?? ResolveWorkingSetBytes;
        _inspectorCandidateResolver = inspectorCandidateResolver ?? DllCandidateResolver.EnumerateInspectorCandidates;
        _bootstrapperCandidateResolver = bootstrapperCandidateResolver ?? DllCandidateResolver.EnumerateBootstrapperCandidates;
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

        return await RunSingleFlightAsync(
            processId.Value,
            cancellationToken,
            sharedCancellationToken => ExecuteForProcessAsync(
                processId.Value,
                explicitProcessSelection,
                selectionStrategy,
                autoDiscoveryResolution,
                sharedCancellationToken)).ConfigureAwait(false);
    }

    private async Task<object> ExecuteForProcessAsync(
        int processId,
        bool explicitProcessSelection,
        ProcessDiscoverySelectionStrategy selectionStrategy,
        AutoDiscoveryResolution? autoDiscoveryResolution,
        CancellationToken cancellationToken)
    {
        var connectStopwatch = Stopwatch.StartNew();

        var rateLimitStatus = _sessionManager.CheckRateLimitStatus(processId);
        if (!rateLimitStatus.Allowed)
        {
            return RateLimitResponseFactory.Create(
                rateLimitStatus,
                "Rate limit exceeded for connect operations. Please slow down your requests.");
        }

        if (_sessionManager.HasSession(processId))
        {
            var existingPipeClient = _sessionManager.GetPipeClient(processId);
            if (existingPipeClient?.IsConnected == true)
            {
                _sessionManager.SetActiveProcess(processId);
                return new { success = true, message = "Already connected to process", processId };
            }

            _sessionManager.RemoveSession(processId);
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

        var validationError = _injector.ValidateTarget(processId);
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

        try
        {
            _sessionManager.EnsureSecureTransportArtifactsCreated();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new
            {
                success = false,
                error = $"Failed to prepare secure transport artifacts: {ex.Message}",
                errorCode = "SecureTransportInitializationFailed",
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
            _sessionManager.GetAuthenticationSecretBase64(),
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

        var injectionResult = _injector.InjectWithBootstrap(injectionRequest, cancellationToken);
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
                    stage = injectionResult.FailedAtStage?.ToString(),
                    exitCode = injectionResult.BootstrapExitCode
                };
            }

            return new
            {
                success = false,
                error = injectionResult.ErrorMessage ?? "Injection failed",
                errorCode = injectionResult.Error switch
                {
                    InjectionError.Timeout or InjectionError.PipeReadyTimeout => "Timeout",
                    _ => injectionResult.Error.ToString()
                },
                stage = injectionResult.FailedAtStage?.ToString(),
                exitCode = injectionResult.BootstrapExitCode
            };
        }

        _sessionManager.AddSession(processId);
        try
        {
            var pipeClient = _sessionManager.GetPipeClient(processId);
            if (pipeClient == null)
            {
                _sessionManager.RemoveSession(processId);
                return new
                {
                    success = false,
                    error = "Failed to create Named Pipe client",
                    errorCode = "InternalError"
                };
            }

            var remainingPipeConnectTimeout = GetRemainingPipeConnectTimeout(
                connectStopwatch.Elapsed,
                TimeSpan.FromSeconds(McpServerConfiguration.ConnectTimeoutSeconds));
            if (remainingPipeConnectTimeout <= TimeSpan.Zero)
            {
                _sessionManager.RemoveSession(processId);
                return new
                {
                    success = false,
                    error = "Connect timed out before the final Inspector Named Pipe handshake could start",
                    errorCode = "Timeout",
                    hint = "The injection phase consumed the full timeout budget. Target process may be slow to load the Inspector DLL."
                };
            }

            var connected = await pipeClient.ConnectAsync(
                remainingPipeConnectTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!connected)
            {
                var pipeConnectFailure = DescribePipeConnectFailure(pipeClient.LastConnectFailure, processId);
                _sessionManager.RemoveSession(processId);
                return new
                {
                    success = false,
                    error = pipeConnectFailure.Error,
                    errorCode = pipeConnectFailure.ErrorCode,
                    hint = pipeConnectFailure.Hint
                };
            }

            _sessionManager.SetActiveProcess(processId);
            if (!explicitProcessSelection &&
                autoDiscoveryResolution?.SelectedCandidate is ProcessDiscoveryCandidateSummary candidate)
            {
                return new
                {
                    success = true,
                    message = "Connected successfully. You can now use inspection tools (get_visual_tree, get_bindings, get_binding_errors, etc.) with this processId.",
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
                message = "Connected successfully. You can now use inspection tools (get_visual_tree, get_bindings, get_binding_errors, etc.) with this processId.",
                processId
            };
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "ConnectTool cleanup triggered for process {0} after pipe handshake failure: {1}: {2}",
                processId,
                ex.GetType().Name,
                ex.Message);
            _sessionManager.RemoveSession(processId);
            throw;
        }
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
