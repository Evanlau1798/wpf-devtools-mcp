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

    /// <summary>
    /// Create ConnectTool with dependency injection
    /// </summary>
    public ConnectTool(
        SessionManager sessionManager,
        IProcessInjector injector,
        WpfProcessDetector? processDetector = null,
        Action<string>? dllPathValidator = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _processDetector = processDetector ?? new WpfProcessDetector();
        _dllPathValidator = dllPathValidator ?? DllPathValidator.ValidateDllPath;
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
        }

        if (!processId.HasValue)
        {
            return new
            {
                success = false,
                error = "Missing required parameter: processId"
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
                return new
                {
                    success = true,
                    message = "Already connected to process",
                    processId = processId.Value
                };
            }

            _sessionManager.RemoveSession(processId.Value);
        }

        var validationError = _injector.ValidateTarget(processId.Value);
        if (validationError != InjectionError.None)
        {
            return new
            {
                success = false,
                error = GetErrorMessage(validationError, processId.Value)
            };
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

    private static string GetErrorMessage(InjectionError error, int processId)
    {
        return error switch
        {
            InjectionError.ProcessNotFound => $"Process {processId} not found or has exited",
            InjectionError.NotWpfApplication => $"Process {processId} is not a WPF application",
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
}
