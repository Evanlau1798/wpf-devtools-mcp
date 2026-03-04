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
            "[Process] List all running WPF processes available for inspection. Returns: processId, processName, windowTitle, architecture (X86/X64/ARM64), dotNetVersion. Pass processId to connect() to begin inspection.",
            new { type = "object", properties = new { nameFilter = new { type = "string", description = "Filter processes by name (case-insensitive substring match)" } } },
            async (args, ct) => await new GetProcessesTool().ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { },
                new { nameFilter = "TestApp" }
            });

        RegisterTool(registry, "connect",
            "[Process] Connect to a WPF application by injecting the Inspector DLL. MUST be called before any other inspection tool. Returns success status. If already connected, returns immediately.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the target WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new ConnectTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "ping",
            "[Process] Check connection health and measure round-trip latency to the Inspector DLL in the target process. Returns latency in milliseconds. Use to verify connection is still alive.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new PingTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });
    }
}
