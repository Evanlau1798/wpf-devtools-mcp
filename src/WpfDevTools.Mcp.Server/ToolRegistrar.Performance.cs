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
            "[Performance] Get render statistics from a WPF application. Returns frame rate, render time, dirty region count, and other WPF rendering pipeline metrics.\n\n" +
            "USE WHEN: UI feels slow or laggy; investigating rendering performance issues.\n" +
            "DO NOT USE: For memory leaks (use find_binding_leaks instead).\n\n" +
            "⚠️ PERFORMANCE: This tool measures rendering metrics over a short period (1-2 seconds).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  frameRate: number,\n" +
            "  avgRenderTime: number (ms),\n" +
            "  dirtyRegionCount: number,\n" +
            "  visualCount: number\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first",
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
            async (args, ct) => await new GetRenderStatsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "find_binding_leaks",
            "[Performance] Detect potential binding memory leaks by tracking live binding references. Threshold is the minimum number of live bindings on a single element to flag as suspicious.\n\n" +
            "USE WHEN: Memory usage grows over time; suspecting binding-related memory leaks.\n" +
            "DO NOT USE: On apps with legitimately many bindings per element (e.g., data grids).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  suspects: [{\n" +
            "    elementId, elementType, bindingCount: number\n" +
            "  }]\n" +
            "}\n\n" +
            "Empty suspects array means no leak candidates found.\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    },
                    threshold = new {
                        type = "integer",
                        description = "Minimum live binding count per element to flag as a leak candidate.",
                        minimum = 1,
                        maximum = 10000,
                        @default = 100
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new FindBindingLeaksTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, threshold = 50 }
            });

        RegisterTool(registry, "measure_element_render_time",
            "[Performance] Measure the render time of a WPF element in milliseconds. Forces a re-render and measures the time taken.\n\n" +
            "USE WHEN: Identifying slow-rendering elements; profiling UI performance.\n" +
            "DO NOT USE: Repeatedly in a loop (causes performance overhead).\n\n" +
            "⚠️ PERFORMANCE: This tool forces a re-render, which may briefly impact UI responsiveness.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  renderTimeMs: number,\n" +
            "  elementId\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    },
                    elementId = new {
                        type = "string",
                        description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window.",
                        @default = (object?)null
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new MeasureElementRenderTimeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_visual_count",
            "[Performance] Get the count of visual elements in a WPF element subtree. High counts (>5000) may indicate performance issues.\n\n" +
            "USE WHEN: UI feels slow; need to identify overly complex subtrees.\n" +
            "DO NOT USE: For memory usage (use find_binding_leaks instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  count: number,\n" +
            "  elementId\n" +
            "}\n\n" +
            "Guideline: <1000 = good, 1000-5000 = acceptable, >5000 = potential performance issue.\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    },
                    elementId = new {
                        type = "string",
                        description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window.",
                        @default = (object?)null
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new GetVisualCountTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" }
            });
    }
}
