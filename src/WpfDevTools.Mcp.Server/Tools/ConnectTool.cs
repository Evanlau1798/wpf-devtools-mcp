using WpfDevTools.Injector;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to connect to a WPF process
/// </summary>
public class ConnectTool
{
    private readonly ProcessInjector _injector;
    private readonly SessionManager _sessionManager;
    private readonly string _inspectorDllPath;

    public ConnectTool(SessionManager? sessionManager = null, string? inspectorDllPath = null)
    {
        _injector = new ProcessInjector();
        _sessionManager = sessionManager ?? new SessionManager();
        _inspectorDllPath = inspectorDllPath ?? GetInspectorDllPath();
    }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        // Parse parameters
        int? processId = null;
        if (parameters != null)
        {
            var paramsType = parameters.GetType();
            var processIdProp = paramsType.GetProperty("processId");
            var processIdValue = processIdProp?.GetValue(parameters);
            if (processIdValue != null)
            {
                processId = Convert.ToInt32(processIdValue);
            }
        }

        if (!processId.HasValue)
        {
            return new
            {
                success = false,
                error = "Missing required parameter: processId"
            };
        }

        // Check if already connected
        if (_sessionManager.HasSession(processId.Value))
        {
            return new
            {
                success = true,
                message = "Already connected to process",
                processId = processId.Value
            };
        }

        // Validate target process
        var validationError = _injector.ValidateTarget(processId.Value);
        if (validationError != InjectionError.None)
        {
            return new
            {
                success = false,
                error = GetErrorMessage(validationError, processId.Value)
            };
        }

        // Perform injection
        var injectionResult = _injector.Inject(processId.Value, _inspectorDllPath);
        if (!injectionResult.Success)
        {
            return new
            {
                success = false,
                error = injectionResult.ErrorMessage ?? "Injection failed"
            };
        }

        // Add to session manager
        _sessionManager.AddSession(processId.Value);

        return new
        {
            success = true,
            message = "Connected successfully",
            processId = processId.Value,
            pipeName = $"WpfDevTools_{processId.Value}"
        };
    }

    private string GetInspectorDllPath()
    {
        // Get the Inspector DLL path relative to the MCP Server executable
        var serverDir = AppContext.BaseDirectory;
        var inspectorDll = Path.Combine(serverDir, "WpfDevTools.Inspector.dll");

        if (!File.Exists(inspectorDll))
        {
            // Fallback: try to find it in the build output
            inspectorDll = Path.Combine(serverDir, "..", "..", "..", "..", "WpfDevTools.Inspector", "bin", "Debug", "net8.0-windows", "WpfDevTools.Inspector.dll");
            inspectorDll = Path.GetFullPath(inspectorDll);
        }

        return inspectorDll;
    }

    private string GetErrorMessage(InjectionError error, int processId)
    {
        return error switch
        {
            InjectionError.ProcessNotFound => $"Process {processId} not found or has exited",
            InjectionError.NotWpfApplication => $"Process {processId} is not a WPF application",
            InjectionError.AccessDenied => $"Access denied to process {processId}. Try running as administrator.",
            InjectionError.ArchitectureMismatch => $"Architecture mismatch for process {processId}",
            _ => $"Validation failed: {error}"
        };
    }
}
