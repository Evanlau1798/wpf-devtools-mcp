using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Injector;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Process Management tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class ProcessMcpTools
{
    private const string TestTrustLocalReleaseSignatureSkipEnvVar = "WPFDEVTOOLS_TEST_TRUST_LOCAL_RELEASE_SIGNATURE_SKIP";

    [McpServerTool(Name = "get_processes", Title = "List Inspectable WPF Processes", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(ProcessMcpToolDescriptions.GetProcesses)]
    public static Task<CallToolResult> GetProcesses(
        [Description("Optional case-insensitive substring filter for the target process name.")] string? nameFilter = null,
        [AllowedValues("visible", "all", "foreground")]
        [Description("Optional window visibility filter: 'visible' (default), 'all', or 'foreground'.")] string? windowFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("nameFilter", nameFilter),
            ("windowFilter", windowFilter));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetProcessesTool>("GetProcessesTool", () => new GetProcessesTool()).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "select_active_process", Title = "Select Active WPF Process", OpenWorld = false, Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description(ProcessMcpToolDescriptions.SelectActiveProcess)]
    public static Task<CallToolResult> SelectActiveProcess(
        SessionManager sessionManager,
        [Range(1, int.MaxValue)]
        [Description("Connected WPF process ID to make active for omitted processId workflows.")] int processId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<SelectActiveProcessTool>(sessionManager, 
                "SelectActiveProcessTool",
                () => new SelectActiveProcessTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_active_process", Title = "Get Active WPF Process", OpenWorld = false, ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(ProcessMcpToolDescriptions.GetActiveProcess)]
    public static Task<CallToolResult> GetActiveProcess(
        SessionManager sessionManager,
        CancellationToken cancellationToken = default)
    {
        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetActiveProcessTool>(sessionManager, 
                "GetActiveProcessTool",
                () => new GetActiveProcessTool(sessionManager)).ExecuteAsync(a, ct),
            null,
            cancellationToken);
    }

    [McpServerTool(Name = "connect", Title = "Connect To Running WPF Process", OpenWorld = false, Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description(ProcessMcpToolDescriptions.Connect)]
    public static Task<CallToolResult> Connect(
        SessionManager sessionManager,
        [Range(1, int.MaxValue)]
        [Description("Optional target WPF process ID returned by get_processes. WPFDEVTOOLS_MCP_ALLOWED_TARGETS must contain the target's exact local absolute executable path or connect will fail closed. Omit to auto-discover when exactly one allowlisted WPF process is running.")] int? processId = null,
        [AllowedValues("single_only", "largest_working_set")]
        [Description("Optional auto-discovery strategy: 'single_only' (safe default) or 'largest_working_set' for multi-process auto-selection.")] string? selectionStrategy = null,
        [AllowedValues("visible", "all", "foreground")]
        [Description("Optional auto-discovery window filter: 'visible' (default), 'all', or 'foreground'.")] string? windowFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("selectionStrategy", selectionStrategy),
            ("windowFilter", windowFilter));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ConnectTool>(sessionManager, "ConnectTool", () => CreateConnectTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            timeoutSeconds: McpServerConfiguration.ConnectTimeoutSeconds,
            toolName: "connect");
    }

    private static ConnectTool CreateConnectTool(SessionManager sessionManager)
    {
        return new ConnectTool(
            sessionManager,
            new ProcessInjector(),
            dllPathValidator: CreateDllPathValidator());
    }

    private static Action<string> CreateDllPathValidator()
    {
        if (IsTestTrustLocalReleaseSignatureSkipEnabled())
        {
            return path => DllPathValidator.ValidateDllPath(
                path,
                AppContext.BaseDirectory,
                trustedLocalDevelopmentSkipOptIn: true);
        }

        return path =>
        {
            if (InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(path, AppContext.BaseDirectory))
            {
                DllPathValidator.ValidateChecksumOnlyReleasePayloadPath(path, AppContext.BaseDirectory);
                return;
            }

            DllPathValidator.ValidateDllPath(path);
        };
    }

    private static bool IsTestTrustLocalReleaseSignatureSkipEnabled()
    {
        var value = Environment.GetEnvironmentVariable(TestTrustLocalReleaseSignatureSkipEnvVar);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || (bool.TryParse(value, out var enabled) && enabled);
    }

    [McpServerTool(Name = "ping", Title = "Ping WPF Inspector Session", OpenWorld = false, ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(ProcessMcpToolDescriptions.Ping)]
    public static Task<CallToolResult> Ping(
        SessionManager sessionManager,
        [Range(1, int.MaxValue)]
        [Description("Optional connected WPF process ID to ping. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<PingTool>(sessionManager, "PingTool", () => new PingTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}



