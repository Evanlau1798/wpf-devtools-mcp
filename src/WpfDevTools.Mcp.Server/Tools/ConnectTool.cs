using System.Text.Json;
using WpfDevTools.Injector;
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
public class ConnectTool
{
    private readonly ProcessInjector _injector;
    private readonly SessionManager _sessionManager;
    private readonly string _inspectorDllPath;

    public ConnectTool(SessionManager sessionManager, string? inspectorDllPath = null)
    {
        _injector = new ProcessInjector();
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

        if (inspectorDllPath != null)
        {
            ValidateDllPath(inspectorDllPath);
            if (!File.Exists(inspectorDllPath))
                throw new FileNotFoundException("Inspector DLL not found", inspectorDllPath);
        }

        _inspectorDllPath = inspectorDllPath ?? GetInspectorDllPath();
        ValidateDllPath(_inspectorDllPath);
    }

    /// <summary>
    /// Execute the tool
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

        // Add to session manager (also creates NamedPipeClient)
        _sessionManager.AddSession(processId.Value);

        // Connect to the Named Pipe
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

        var connected = await pipeClient.ConnectAsync(InspectorConfig.PipeConnectTimeout).ConfigureAwait(false);
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

    private static string GetInspectorDllPath()
    {
        var serverDir = AppContext.BaseDirectory;
        var inspectorDll = Path.Combine(serverDir, "WpfDevTools.Inspector.dll");

        if (File.Exists(inspectorDll))
            return inspectorDll;

        // Fallback to development path
        var fallbackDll = Path.Combine(serverDir, "..", "..", "..", "..", "WpfDevTools.Inspector", "bin", "Debug", "net8.0-windows", "WpfDevTools.Inspector.dll");
        fallbackDll = Path.GetFullPath(fallbackDll);

        if (File.Exists(fallbackDll))
            return fallbackDll;

        // Don't expose full path in exception message for security
        // Log the actual path internally if needed
        throw new FileNotFoundException("Inspector DLL not found. Please ensure the application is built correctly.");
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

    /// <summary>
    /// Validate DLL path to prevent security issues
    /// </summary>
    private static void ValidateDllPath(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath))
            throw new ArgumentException("DLL path cannot be empty", nameof(dllPath));

        // Check if path has .dll extension
        if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DLL path must have .dll extension", nameof(dllPath));

        // Prevent network paths
        var uri = new Uri(dllPath, UriKind.RelativeOrAbsolute);
        if (uri.IsAbsoluteUri && uri.IsUnc)
            throw new ArgumentException("Network paths are not allowed", nameof(dllPath));

        // Get full path to normalize
        var fullPath = Path.GetFullPath(dllPath);

        // Prevent system directories
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (fullPath.StartsWith(systemDir, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Cannot load DLL from system directories", nameof(dllPath));
        }
    }
}
