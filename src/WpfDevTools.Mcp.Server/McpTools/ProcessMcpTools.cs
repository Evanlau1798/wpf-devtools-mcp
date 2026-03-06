using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Process Management tools (3 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class ProcessMcpTools
{
    [McpServerTool(Name = "get_processes", ReadOnly = true)]
    [Description(
        "[Process] List all running WPF processes available for inspection. " +
        "Returns: processId, processName, windowTitle, architecture (X86/X64/ARM64), dotNetVersion.\n\n" +
        "USE WHEN: Starting a new inspection session; discovering available WPF applications.\n" +
        "DO NOT USE: Repeatedly in a loop (process list changes infrequently).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  processes: [{\n" +
        "    processId: integer,\n" +
        "    processName: string,\n" +
        "    windowTitle: string,\n" +
        "    architecture: 'X86'|'X64'|'ARM64',\n" +
        "    dotNetVersion: string\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"access denied\" -> run MCP server as administrator\n\n" +
        "Examples:\n" +
        "- { }\n" +
        "- { nameFilter: \"TestApp\" }")]
    public static Task<CallToolResult> GetProcesses(
        string? nameFilter = null)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("nameFilter", nameFilter));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetProcessesTool().ExecuteAsync(a, ct),
            args,
            CancellationToken.None);
    }

    [McpServerTool(Name = "connect", Idempotent = true)]
    [Description(
        "[Process] Connect to a WPF application by injecting the Inspector DLL. " +
        "MUST be called before any other inspection tool. Returns success status.\n\n" +
        "USE WHEN: After get_processes, before using any inspection tools.\n" +
        "DO NOT USE: On already-connected processes (returns immediately with success=true).\n\n" +
        "TIMEOUT: Connection attempt times out after 30 seconds.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  connected: boolean,\n" +
        "  message: string\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"access denied\" -> run MCP server as administrator\n" +
        "- \"not a WPF application\" -> processId is not a WPF app (use get_processes)\n" +
        "- \"architecture mismatch\" -> server and target app must match (x64 vs x86)\n" +
        "- \"timeout\" -> connection took >30s, process may be unresponsive\n" +
        "- \"processId required\" -> must specify which process to connect to\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> Connect(
        SessionManager sessionManager,
        int processId)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new ConnectTool(sessionManager).ExecuteAsync(a, ct),
            args,
            CancellationToken.None);
    }

    [McpServerTool(Name = "ping", Idempotent = true)]
    [Description(
        "[Process] Check connection health and measure round-trip latency to the Inspector DLL " +
        "in the target process. Returns latency in milliseconds.\n\n" +
        "USE WHEN: Verifying connection is still alive; measuring IPC performance.\n" +
        "DO NOT USE: Before calling connect() (will fail).\n\n" +
        "TIMEOUT: Ping times out after 5 seconds.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  latencyMs: number,\n" +
        "  connected: boolean\n" +
        "}\n\n" +
        "Typical latency: 0.1-1ms (Named Pipes). >100ms indicates performance issues.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"timeout\" -> process may be frozen or unresponsive\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> Ping(
        SessionManager sessionManager,
        int processId)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new PingTool(sessionManager).ExecuteAsync(a, ct),
            args,
            CancellationToken.None);
    }
}
