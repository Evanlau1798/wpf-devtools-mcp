using System.Text.Json;
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

    public ConnectTool(SessionManager sessionManager, string? inspectorDllPath = null)
    {
        _injector = new ProcessInjector();
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _inspectorDllPath = inspectorDllPath ?? GetInspectorDllPath();
    }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        int? processId = null;
        if (arguments.HasValue && arguments.Value.TryGetProperty("processId", out var pidProp))
            processId = pidProp.GetInt32();

        if (!processId.HasValue)
        {
            return Task.FromResult<object>(new
            {
                success = false,
                error = "Missing required parameter: processId"
            });
        }

        // Check if already connected
        if (_sessionManager.HasSession(processId.Value))
        {
            return Task.FromResult<object>(new
            {
                success = true,
                message = "Already connected to process",
                processId = processId.Value
            });
        }

        // Validate target process
        var validationError = _injector.ValidateTarget(processId.Value);
        if (validationError != InjectionError.None)
        {
            return Task.FromResult<object>(new
            {
                success = false,
                error = GetErrorMessage(validationError, processId.Value)
            });
        }

        // Perform injection
        var injectionResult = _injector.Inject(processId.Value, _inspectorDllPath);
        if (!injectionResult.Success)
        {
            return Task.FromResult<object>(new
            {
                success = false,
                error = injectionResult.ErrorMessage ?? "Injection failed"
            });
        }

        // Add to session manager (also creates NamedPipeClient)
        _sessionManager.AddSession(processId.Value);

        return Task.FromResult<object>(new
        {
            success = true,
            message = "Connected successfully",
            processId = processId.Value,
            pipeName = $"WpfDevTools_{processId.Value}"
        });
    }

    private static string GetInspectorDllPath()
    {
        var serverDir = AppContext.BaseDirectory;
        var inspectorDll = Path.Combine(serverDir, "WpfDevTools.Inspector.dll");

        if (!File.Exists(inspectorDll))
        {
            inspectorDll = Path.Combine(serverDir, "..", "..", "..", "..", "WpfDevTools.Inspector", "bin", "Debug", "net8.0-windows", "WpfDevTools.Inspector.dll");
            inspectorDll = Path.GetFullPath(inspectorDll);
        }

        return inspectorDll;
    }

    private static string GetErrorMessage(InjectionError error, int processId)
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
