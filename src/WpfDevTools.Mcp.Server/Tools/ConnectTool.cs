using System.Diagnostics;
using System.Text.Json;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Configuration;

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
public sealed class ConnectTool
{
    private readonly IProcessInjector _injector;
    private readonly SessionManager _sessionManager;
    private readonly WpfProcessDetector _processDetector;
    private readonly Action<string> _dllPathValidator;
    private readonly Func<bool> _isCurrentProcessElevated;
    private readonly Func<int, long> _workingSetResolver;

    /// <summary>
    /// Create ConnectTool with dependency injection
    /// </summary>
    public ConnectTool(
        SessionManager sessionManager,
        IProcessInjector injector,
        WpfProcessDetector? processDetector = null,
        Action<string>? dllPathValidator = null,
        Func<bool>? isCurrentProcessElevated = null,
        Func<int, long>? workingSetResolver = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _processDetector = processDetector ?? new WpfProcessDetector();
        _dllPathValidator = dllPathValidator ?? DllPathValidator.ValidateDllPath;
        _isCurrentProcessElevated = isCurrentProcessElevated ?? CurrentProcessElevationDetector.IsCurrentProcessElevated;
        _workingSetResolver = workingSetResolver ?? ResolveWorkingSetBytes;
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
                    error = "processId must be a valid 32-bit integer"
                };
            }
            processId = parsedPid;
            explicitProcessSelection = true;
        }

        var selectionStrategyValue = arguments.HasValue && arguments.Value.TryGetProperty("selectionStrategy", out var strategyProp)
            ? strategyProp.GetString()
            : null;
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

        var windowFilterValue = arguments.HasValue && arguments.Value.TryGetProperty("windowFilter", out var windowFilterProp)
            ? windowFilterProp.GetString()
            : null;
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
                error = "processId must be a positive integer"
            };
        }

        var connectStopwatch = Stopwatch.StartNew();

        if (!_sessionManager.CheckRateLimit(processId.Value))
        {
            var availableTokens = _sessionManager.GetAvailableTokens(processId.Value);
            return new
            {
                success = false,
                error = "Rate limit exceeded for connect operations. Please slow down your requests.",
                availableTokens,
                retryAfterSeconds = 60,
                retryAfter = "Wait 1 minute for rate limit to reset"
            };
        }

        if (_sessionManager.HasSession(processId.Value))
        {
            var existingPipeClient = _sessionManager.GetPipeClient(processId.Value);
            if (existingPipeClient?.IsConnected == true)
            {
                _sessionManager.SetActiveProcess(processId.Value);
                return new
                {
                    success = true,
                    message = "Already connected to process",
                    processId = processId.Value
                };
            }

            _sessionManager.RemoveSession(processId.Value);
        }

        var processInfo = _processDetector.GetProcessInfo(processId.Value);
        if (processInfo == null)
        {
            return new
            {
                success = false,
                error = $"Could not detect process info for {processId.Value}"
            };
        }

        var access = ProcessConnectionAccessEvaluator.Evaluate(
            processId.Value,
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

        var validationError = _injector.ValidateTarget(processId.Value);
        if (validationError != InjectionError.None)
        {
            return new
            {
                success = false,
                error = GetErrorMessage(validationError, processId.Value, processInfo),
                errorCode = validationError.ToString(),
                targetIsElevated = processInfo.IsElevated,
                requiresElevationToConnect = access.RequiresElevationToConnect,
                canConnectFromCurrentServer = access.CanConnectFromCurrentServer
            };
        }

        var inspectorCandidates = DllCandidateResolver.EnumerateInspectorCandidates(
            AppContext.BaseDirectory).Where(File.Exists).ToArray();
        var bootstrapperCandidates = DllCandidateResolver.EnumerateBootstrapperCandidates(
            AppContext.BaseDirectory).Where(File.Exists).ToArray();

        var injectionRequest = InjectionPlanFactory.CreateRequest(
            processInfo, inspectorCandidates, bootstrapperCandidates);

        if (injectionRequest == null)
        {
            return new
            {
                success = false,
                error = "No matching Inspector or Bootstrapper DLL found for target process. " +
                    $"Runtime: {processInfo.Runtime}, Architecture: {processInfo.Architecture}"
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
                    error = GetErrorMessage(InjectionError.AccessDenied, processId.Value, processInfo),
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
                stage = injectionResult.FailedAtStage?.ToString(),
                exitCode = injectionResult.BootstrapExitCode
            };
        }

        _sessionManager.AddSession(processId.Value);
        try
        {
            var pipeClient = _sessionManager.GetPipeClient(processId.Value);
            if (pipeClient == null)
            {
                _sessionManager.RemoveSession(processId.Value);
                return new
                {
                    success = false,
                    error = "Failed to create Named Pipe client"
                };
            }

            var remainingPipeConnectTimeout = GetRemainingPipeConnectTimeout(
                connectStopwatch.Elapsed,
                TimeSpan.FromSeconds(McpServerConfiguration.ConnectTimeoutSeconds));
            if (remainingPipeConnectTimeout <= TimeSpan.Zero)
            {
                _sessionManager.RemoveSession(processId.Value);
                return new
                {
                    success = false,
                    error = "Connect timed out before the final Inspector Named Pipe handshake could start"
                };
            }

            var connected = await pipeClient.ConnectAsync(
                remainingPipeConnectTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!connected)
            {
                _sessionManager.RemoveSession(processId.Value);
                return new
                {
                    success = false,
                    error = "Timeout connecting to Inspector Named Pipe"
                };
            }

            _sessionManager.SetActiveProcess(processId.Value);

            if (!explicitProcessSelection && autoDiscoveryResolution?.SelectedCandidate is ProcessDiscoveryCandidateSummary candidate)
            {
                return new
                {
                    success = true,
                    message = "Connected successfully. You can now use inspection tools (get_visual_tree, get_bindings, get_binding_errors, etc.) with this processId.",
                    processId = processId.Value,
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
                processId = processId.Value
            };
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "ConnectTool cleanup triggered for process {0} after pipe handshake failure: {1}: {2}",
                processId.Value,
                ex.GetType().Name,
                ex.Message);
            _sessionManager.RemoveSession(processId.Value);
            throw;
        }
    }

    private static string GetErrorMessage(InjectionError error, int processId, WpfProcessInfo? processInfo)
    {
        return error switch
        {
            InjectionError.ProcessNotFound => $"Process {processId} not found or has exited",
            InjectionError.NotWpfApplication => $"Process {processId} is not a WPF application",
            InjectionError.AccessDenied when processInfo?.IsElevated == true =>
                $"Access denied to process {processId} because the target is elevated. Restart the MCP server as administrator to connect or control this WPF process.",
            InjectionError.AccessDenied => $"Access denied to process {processId}. Try running as administrator.",
            InjectionError.ArchitectureMismatch => $"Architecture mismatch for process {processId}. Ensure the MCP server architecture matches the target process (both x64 or both x86).",
            _ => $"Validation failed: {error}"
        };
    }

    internal static TimeSpan GetRemainingPipeConnectTimeout(
        TimeSpan elapsed,
        TimeSpan totalTimeout)
    {
        var remaining = totalTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    internal static void ValidateDllPath(string dllPath)
        => DllPathValidator.ValidateDllPath(dllPath);

    private AutoDiscoveryResolution TryResolveAutoDiscoveredProcess(
        ProcessDiscoverySelectionStrategy selectionStrategy,
        ProcessWindowFilter windowFilter)
    {
        var currentProcessIsElevated = _isCurrentProcessElevated();
        var candidates = _processDetector
            .GetAllWpfProcesses(windowFilter)
            .Select(process =>
            {
                var access = ProcessConnectionAccessEvaluator.Evaluate(
                    process.ProcessId,
                    process.IsElevated,
                    currentProcessIsElevated);
                return new ProcessDiscoveryCandidateSummary(
                    process.ProcessId,
                    process.ProcessName,
                    process.WindowTitle,
                    _workingSetResolver(process.ProcessId),
                    process.IsElevated,
                    access.RequiresElevationToConnect,
                    access.CanConnectFromCurrentServer,
                    access.ConnectionWarning);
            })
            .OrderByDescending(candidate => candidate.WorkingSetBytes)
            .ThenBy(candidate => candidate.ProcessId)
            .ToArray();

        if (candidates.Length == 0)
        {
            return new AutoDiscoveryResolution(
                null,
                new
                {
                    success = false,
                    error = "No running WPF processes were found for the requested window filter. Start the target app or call get_processes() to confirm availability.",
                    errorCode = "NoWpfProcessesFound",
                    hint = "Launch the WPF app first, retry connect(windowFilter='all') to include background targets, or call get_processes() for manual discovery."
                },
                0,
                candidates,
                null,
                false);
        }

        if (candidates.Length == 1)
        {
            return new AutoDiscoveryResolution(
                candidates[0].ProcessId,
                null,
                1,
                candidates,
                candidates[0],
                false);
        }

        if (selectionStrategy != ProcessDiscoverySelectionStrategy.LargestWorkingSet)
        {
            return new AutoDiscoveryResolution(
                null,
                new
                {
                    success = false,
                    error = "Multiple WPF processes found; specify processId or use selectionStrategy='largest_working_set'.",
                    errorCode = "MultipleWpfProcessesFound",
                    candidateCount = candidates.Length,
                    processes = candidates.Select(ToContractCandidate).ToArray(),
                    hint = "Call connect(processId) for a specific target, or retry connect(selectionStrategy='largest_working_set') if the largest process is acceptable."
                },
                candidates.Length,
                candidates,
                null,
                false);
        }

        return new AutoDiscoveryResolution(
            candidates[0].ProcessId,
            null,
            candidates.Length,
            candidates,
            candidates[0],
            true);
    }

    private static object ToContractCandidate(ProcessDiscoveryCandidateSummary candidate)
    {
        return new
        {
            processId = candidate.ProcessId,
            processName = candidate.ProcessName,
            windowTitle = candidate.WindowTitle,
            workingSetBytes = candidate.WorkingSetBytes,
            isElevated = candidate.IsElevated,
            requiresElevationToConnect = candidate.RequiresElevationToConnect,
            canConnectFromCurrentServer = candidate.CanConnectFromCurrentServer,
            connectionWarning = candidate.ConnectionWarning
        };
    }

    private static long ResolveWorkingSetBytes(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    private sealed record AutoDiscoveryResolution(
        int? ProcessId,
        object? ErrorResult,
        int CandidateCount,
        IReadOnlyList<ProcessDiscoveryCandidateSummary> Candidates,
        ProcessDiscoveryCandidateSummary? SelectedCandidate,
        bool AutoSelected);
}
