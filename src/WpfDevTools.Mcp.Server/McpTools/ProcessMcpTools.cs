using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Process Management tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class ProcessMcpTools
{
    private const string ProcessMetadata = "CATEGORY: Process | SAFETY: Check the SDK ReadOnly and Destructive flags before invoking this tool.\n\n";
    [McpServerTool(Name = "get_processes", Title = "List Inspectable WPF Processes", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to resolve target ambiguity after connect() reports multiple candidates, or when you explicitly need process metadata before connecting.\n\n" +
        ProcessMetadata + "[Process] List all running WPF processes available for inspection. " +
        "Returns: processId, processName, windowTitle, architecture (X86/X64/ARM64), dotNetVersion, runtime, isElevated, requiresElevationToConnect, canConnectFromCurrentServer, connectionWarning.\n\n" +
        "USE WHEN: connect() reports multiple candidates; you need architecture/elevation/window metadata before choosing a target; you want explicit visible/all/foreground filtering before connecting.\n" +
        "DO NOT USE: As the default first step when connect() auto-discovery can already resolve the target, or repeatedly in a loop (process list changes infrequently).\n\n" +
        "WINDOW FILTERS:\n" +
        "- Omit windowFilter for the visible-only default\n" +
        "- Use windowFilter='all' to include background or hidden WPF windows\n" +
        "- Use windowFilter='foreground' to restrict results to the active foreground WPF window\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  processes: [{\n" +
        "    processId: integer,\n" +
        "    processName: string,\n" +
        "    windowTitle: string,\n" +
        "    architecture: 'X86'|'X64'|'ARM64',\n" +
        "    dotNetVersion: string,\n" +
        "    runtime: 'Unknown'|'NetFramework'|'NetCore',\n" +
        "    isElevated: boolean,\n" +
        "    requiresElevationToConnect: boolean,\n" +
        "    canConnectFromCurrentServer: boolean,\n" +
        "    connectionWarning: string|null\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"access denied\" -> run MCP server as administrator\n" +
        "- invalid windowFilter -> use 'visible', 'all', or 'foreground'\n\n" +
        "EXAMPLES:\n" +
        "- { }\n" +
        "- { nameFilter: \"TestApp\" }\n" +
        "- { windowFilter: \"foreground\" }")]
    public static Task<CallToolResult> GetProcesses(
        [Description("Optional case-insensitive substring filter for the target process name.")] string? nameFilter = null,
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

    [McpServerTool(Name = "select_active_process", Title = "Select Active WPF Process", OpenWorld = false, Destructive = true, Idempotent = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to explicitly choose which connected WPF process should be used when later tool calls omit processId.\n\n" +
        ProcessMetadata + "[Process] Set the active connected process for processId-omission workflows.\n\n" +
        "USE WHEN: Multiple WPF sessions are connected and you want one explicit default target.\n" +
        "DO NOT USE: Before connect(processId) has succeeded for the chosen process.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{ success: boolean, processId?: number, message?: string, error?: string, errorCode?: string }\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> SelectActiveProcess(
        SessionManager sessionManager,
        [Description("Connected WPF process ID to make active for omitted processId workflows.")] int processId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<SelectActiveProcessTool>(
                "SelectActiveProcessTool",
                () => new SelectActiveProcessTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_active_process", Title = "Get Active WPF Process", OpenWorld = false, ReadOnly = true, Idempotent = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to verify which connected WPF process is currently active for omitted processId workflows.\n\n" +
        ProcessMetadata + "[Process] Returns the active selected process, if any.\n\n" +
        "USE WHEN: Verifying session state before omitting processId in later calls.\n" +
        "DO NOT USE: As a substitute for connect(), or when you already pass processId explicitly on every later tool call.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{ success: boolean, hasActiveProcess: boolean, processId?: number, selectedAtUtc?: string }\n\n" +
        "EXAMPLES:\n" +
        "- { }")]
    public static Task<CallToolResult> GetActiveProcess(
        SessionManager sessionManager,
        CancellationToken cancellationToken = default)
    {
        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetActiveProcessTool>(
                "GetActiveProcessTool",
                () => new GetActiveProcessTool(sessionManager)).ExecuteAsync(a, ct),
            null,
            cancellationToken);
    }

    [McpServerTool(Name = "connect", Title = "Connect To Running WPF Process", OpenWorld = false, Destructive = true, Idempotent = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to connect to a running WPF process before any inspection tool is used.\n\n" +
        ProcessMetadata + "[Process] Connect to a WPF application by injecting the Inspector DLL. " +
        "MUST be called before any other inspection tool. Returns success status.\n\n" +
        "USE WHEN: Before using any inspection tools. If processId is omitted, connect auto-discovers the target when exactly one WPF process is running under the chosen window filter.\n" +
        "DO NOT USE: On already-connected processes (returns immediately with success=true).\n\n" +
        "TIMEOUT: Connection attempt times out after 30 seconds.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  message?: string,\n" +
        "  error?: string,\n" +
        "  errorCode?: string,\n" +
        "  processId?: number,\n" +
        "  processName?: string,\n" +
        "  windowTitle?: string,\n" +
        "  autoDiscovered?: boolean,\n" +
        "  autoSelected?: boolean,\n" +
        "  selectionReason?: 'largest_working_set'|'single_only',\n" +
        "  candidateCount?: number,\n" +
        "  processes?: [{ processId, processName, windowTitle, workingSetBytes, isElevated, requiresElevationToConnect, canConnectFromCurrentServer, connectionWarning }],\n" +
        "  targetIsElevated?: boolean,\n" +
        "  requiresElevationToConnect?: boolean,\n" +
        "  canConnectFromCurrentServer?: boolean,\n" +
        "  suggestedAction?: string\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"access denied\" -> run MCP server as administrator\n" +
        "- elevated target + non-admin server -> preflight denial with requiresElevationToConnect=true and suggestedAction\n" +
        "- \"not a WPF application\" -> processId is not a WPF app (use get_processes)\n" +
        "- \"architecture mismatch\" -> server and target app must match (x64 vs x86)\n" +
        "- \"timeout\" -> connection took >30s, process may be unresponsive\n" +
        "- multiple WPF processes + no processId -> returns errorCode=MultipleWpfProcessesFound and a candidate process list\n" +
        "- 0 WPF processes + no processId -> returns errorCode=NoWpfProcessesFound\n\n" +
        "AUTO-DISCOVERY:\n" +
        "- Omit processId to auto-connect when exactly one WPF process is available\n" +
        "- Omit windowFilter for the visible-only default\n" +
        "- Use windowFilter='all' to include background or hidden WPF windows during auto-discovery\n" +
        "- Use windowFilter='foreground' to restrict auto-discovery to the active foreground WPF window\n" +
        "- Use selectionStrategy='largest_working_set' to auto-select the largest candidate when multiple WPF processes are present\n" +
        "- Keep the safe default by omitting selectionStrategy or using selectionStrategy='single_only'\n\n" +
        "EXAMPLES:\n" +
        "- { }\n" +
        "- { processId: 12345 }\n" +
        "- { selectionStrategy: \"largest_working_set\" }\n" +
        "- { windowFilter: \"all\" }")]
    public static Task<CallToolResult> Connect(
        SessionManager sessionManager,
        [Description("Optional target WPF process ID returned by get_processes. Omit to auto-discover when exactly one WPF process is running.")] int? processId = null,
        [Description("Optional auto-discovery strategy: 'single_only' (safe default) or 'largest_working_set' for multi-process auto-selection.")] string? selectionStrategy = null,
        [Description("Optional auto-discovery window filter: 'visible' (default), 'all', or 'foreground'.")] string? windowFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("selectionStrategy", selectionStrategy),
            ("windowFilter", windowFilter));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ConnectTool>("ConnectTool", () => new ConnectTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            timeoutSeconds: McpServerConfiguration.ConnectTimeoutSeconds);
    }

    [McpServerTool(Name = "ping", Title = "Ping WPF Inspector Session", OpenWorld = false, ReadOnly = true, Idempotent = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to verify a connected WPF inspector session is still healthy before deeper runtime inspection.\n\n" +
        ProcessMetadata + "[Process] Check connection health and measure round-trip latency to the Inspector DLL " +
        "in the target process. Returns latency in milliseconds.\n\n" +
        "USE WHEN: Verifying connection is still alive; measuring IPC performance.\n" +
        "DO NOT USE: Before calling connect() (will fail).\n\n" +
        "TIMEOUT: Ping times out after 5 seconds.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  status: string,\n" +
        "  processId: number,\n" +
        "  latencyMs: number,\n" +
        "  lastActivity: string\n" +
        "}\n\n" +
        "Typical latency: 0.1-1ms (Named Pipes). >100ms indicates performance issues.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"timeout\" -> process may be frozen or unresponsive\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> Ping(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID to ping. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<PingTool>("PingTool", () => new PingTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}



