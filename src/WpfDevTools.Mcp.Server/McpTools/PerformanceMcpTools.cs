using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

#pragma warning disable CS1591 // McpTools use [Description] attribute instead of XML doc comments

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Performance tools (4 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class PerformanceMcpTools
{
    [McpServerTool(Name = "get_render_stats", OpenWorld = false, ReadOnly = true)]
    [Description(
        "[Performance] Get render statistics from a WPF application. Returns frame rate, " +
        "render time, dirty region count, and other WPF rendering pipeline metrics.\n\n" +
        "USE WHEN: UI feels slow or laggy; investigating rendering performance issues.\n" +
        "DO NOT USE: For memory leaks (use find_binding_leaks instead).\n\n" +
        "PERFORMANCE: This tool measures rendering metrics over a short period (1-2 seconds).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  frameRate: number,\n" +
        "  avgRenderTime: number (ms),\n" +
        "  dirtyRegionCount: number,\n" +
        "  visualCount: number\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> GetRenderStats(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetRenderStatsTool>("GetRenderStatsTool", () => new GetRenderStatsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "find_binding_leaks", OpenWorld = false, ReadOnly = true)]
    [Description(
        "[Performance] Detect potential binding memory leaks by tracking live binding references. " +
        "Threshold is the minimum number of live bindings on a single element to flag as suspicious.\n\n" +
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
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, threshold: 50 }")]
    public static Task<CallToolResult> FindBindingLeaks(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional minimum live-binding count that should be flagged as suspicious.")] int? threshold = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("threshold", threshold));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<FindBindingLeaksTool>("FindBindingLeaksTool", () => new FindBindingLeaksTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "measure_element_render_time", OpenWorld = false, ReadOnly = false)]
    [Description(
        "[Performance] Measure the render time of a WPF element in milliseconds. " +
        "Forces a re-render and measures the time taken.\n\n" +
        "USE WHEN: Identifying slow-rendering elements; profiling UI performance.\n" +
        "DO NOT USE: Repeatedly in a loop (causes performance overhead).\n\n" +
        "PERFORMANCE: This tool forces a re-render, which may briefly impact UI responsiveness.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  renderTimeMs: number,\n" +
        "  elementId\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> MeasureElementRenderTime(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional element ID whose render time should be measured. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<MeasureElementRenderTimeTool>("MeasureElementRenderTimeTool", () => new MeasureElementRenderTimeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_visual_count", OpenWorld = false, ReadOnly = true)]
    [Description(
        "[Performance] Get the count of visual elements in a WPF element subtree. " +
        "High counts (>5000) may indicate performance issues.\n\n" +
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
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
    public static Task<CallToolResult> GetVisualCount(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional element ID whose visual subtree should be counted. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetVisualCountTool>("GetVisualCountTool", () => new GetVisualCountTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
