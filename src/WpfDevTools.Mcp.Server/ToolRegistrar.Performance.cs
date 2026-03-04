using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Performance tools registration (4 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 10. Performance (4 tools) ===
    private static void RegisterPerformanceTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_render_stats",
            "[Performance] Get render statistics from a WPF application. Returns frame rate, render time, dirty region count, and other WPF rendering pipeline metrics. Use as a first step when investigating slow UI.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new GetRenderStatsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "find_binding_leaks",
            "[Performance] Detect potential binding memory leaks by tracking live binding references. Threshold is the minimum number of live bindings on a single element to flag as suspicious (default: 100).",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, threshold = new { type = "integer", description = "Minimum live binding count per element to flag as a leak candidate. Default: 100" } }, required = new[] { "processId" } },
            async (args, ct) => await new FindBindingLeaksTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, threshold = 50 }
            });

        RegisterTool(registry, "measure_element_render_time",
            "[Performance] Measure the render time of a WPF element in milliseconds. Forces a re-render and measures the time taken. Use to identify slow-rendering elements.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new MeasureElementRenderTimeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_visual_count",
            "[Performance] Get the count of visual elements in a WPF element subtree. High counts (>5000) may indicate performance issues. Use to identify overly complex UI subtrees.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetVisualCountTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" }
            });
    }
}
