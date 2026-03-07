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

    /// <summary>
    /// Create ConnectTool with dependency injection
    /// </summary>
    public ConnectTool(
        SessionManager sessionManager,
        IProcessInjector injector,
        WpfProcessDetector? processDetector = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _processDetector = processDetector ?? new WpfProcessDetector();
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

        // SECURITY: Rate limit connect attempts to prevent DoS
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
            return new
            {
                success = true,
                message = "Already connected to process",
                processId = processId.Value
            };
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

        // Build injection plan dynamically based on target process info
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

        // SECURITY: Validate resolved DLL paths before injection
        DllPathValidator.ValidateDllPath(injectionRequest.InspectorDllPath);
        DllPathValidator.ValidateDllPath(injectionRequest.BootstrapperDllPath);

        // Perform bootstrap injection
        var injectionResult = _injector.InjectWithBootstrap(injectionRequest);
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

        // Add to session manager (also creates NamedPipeClient)
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

            var connected = await pipeClient.ConnectAsync(
                TimeSpan.FromSeconds(McpServerConfiguration.ConnectTimeoutSeconds),
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
        catch
        {
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

    /// <summary>
    /// Validate DLL path. Delegates to DllPathValidator.
    /// Kept as a forwarding method for backward compatibility with existing tests.
    /// </summary>
    internal static void ValidateDllPath(string dllPath)
        => DllPathValidator.ValidateDllPath(dllPath);
}
