using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Process Management tools registration (3 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 1. Process Management (3 tools) ===
    private static void RegisterProcessTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_processes",
            "[Process] List all running WPF processes available for inspection. Returns: processId, processName, windowTitle, architecture (X86/X64/ARM64), dotNetVersion.\n\n" +
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
            "- \"access denied\" → run MCP server as administrator",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    nameFilter = new {
                        type = "string",
                        description = "Filter processes by name (case-insensitive substring match). Example: 'TestApp' matches 'WpfTestApp.exe'",
                        @default = (object?)null
                    }
                }
            },
            async (args, ct) => await new GetProcessesTool().ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { },
                new { nameFilter = "TestApp" }
            });

        RegisterTool(registry, "connect",
            "[Process] Connect to a WPF application by injecting the Inspector DLL. MUST be called before any other inspection tool. Returns success status.\n\n" +
            "USE WHEN: After get_processes, before using any inspection tools.\n" +
            "DO NOT USE: On already-connected processes (returns immediately with success=true).\n\n" +
            "⚠️ TIMEOUT: Connection attempt times out after 30 seconds.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  connected: boolean,\n" +
            "  message: string\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"access denied\" → run MCP server as administrator\n" +
            "- \"not a WPF application\" → processId is not a WPF app (use get_processes)\n" +
            "- \"architecture mismatch\" → server and target app must match (x64 vs x86)\n" +
            "- \"timeout\" → connection took >30s, process may be unresponsive\n" +
            "- \"processId required\" → must specify which process to connect to",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the target WPF application (from get_processes)"
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new ConnectTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "ping",
            "[Process] Check connection health and measure round-trip latency to the Inspector DLL in the target process. Returns latency in milliseconds.\n\n" +
            "USE WHEN: Verifying connection is still alive; measuring IPC performance.\n" +
            "DO NOT USE: Before calling connect() (will fail).\n\n" +
            "⚠️ TIMEOUT: Ping times out after 5 seconds.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  latencyMs: number,\n" +
            "  connected: boolean\n" +
            "}\n\n" +
            "Typical latency: 0.1-1ms (Named Pipes). >100ms indicates performance issues.\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"timeout\" → process may be frozen or unresponsive",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new PingTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });
    }
}
