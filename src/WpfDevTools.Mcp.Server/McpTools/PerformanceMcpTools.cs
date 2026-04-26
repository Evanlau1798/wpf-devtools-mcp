using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Performance tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class PerformanceMcpTools
{
    private const string PerformanceMetadata = "CATEGORY: Performance\n\n";
    [McpServerTool(Name = "get_render_stats", Title = "Inspect WPF Render Stats", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to inspect WPF render statistics when runtime UI performance feels slow.\n\n" +
        PerformanceMetadata + "[Performance] Get render statistics from a WPF application. Returns frame rate, " +
        "render time, dirty region count, and other WPF rendering pipeline metrics.\n\n" +
        "USE WHEN: UI feels slow or laggy; investigating rendering performance issues.\n" +
        "DO NOT USE: For memory leaks (use find_binding_leaks instead).\n\n" +
        "PERFORMANCE: This tool measures rendering metrics over a short period (1-2 seconds).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  isWarmedUp: boolean,\n" +
        "  confidence: 'low'|'medium'|'high',\n" +
        "  warmUpApplied: boolean,\n" +
        "  minimumRecommendedSampleCount: number,\n" +
        "  minimumRecommendedMonitoringDurationMs: number,\n" +
        "  sampleGuidance: string,\n" +
        "  sampleCount: number,\n" +
        "  sampleWindowSize: number,\n" +
        "  frameRate: number,\n" +
        "  avgRenderTime: number (ms),\n" +
        "  dirtyRegionCount: number,\n" +
        "  totalFrames: number,\n" +
        "  monitoringDuration: number,\n" +
        "  visualCount: number,\n" +
        "  visualCountLimit: number,\n" +
        "  visualCountTruncated: boolean\n" +
        "}\n\n" +
        "NOTE: The first call may return zeros with a 'Monitoring started' message because the render stats " +
        "listener needs time to collect data. Set warmUp=true to wait for a baseline sample window on the same call, or call again after 1-2 seconds.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, warmUp: true }")]
    public static Task<CallToolResult> GetRenderStats(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("When true, waits for a baseline render sampling window before returning so the first call is less likely to be low-confidence.")] bool warmUp = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("warmUp", warmUp));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetRenderStatsTool>("GetRenderStatsTool", () => new GetRenderStatsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "find_binding_leaks", Title = "Find WPF Binding Leaks", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to inspect suspected WPF binding leaks in long-lived runtime sessions.\n\n" +
        PerformanceMetadata + "[Performance] Detect potential binding memory leaks by tracking live binding references. " +
        "Threshold is the minimum number of live bindings on a single element to flag as suspicious.\n\n" +
        "USE WHEN: Memory usage grows over time; suspecting binding-related memory leaks.\n" +
        "DO NOT USE: On apps with legitimately many bindings per element (e.g., data grids).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  confidence: 'low'|'medium'|'high',\n" +
        "  warmUpApplied: boolean,\n" +
        "  samplingDurationMs: number,\n" +
        "  minimumRecommendedSamplingDurationMs: number,\n" +
        "  sampleGuidance: string,\n" +
        "  totalTracked: number,\n" +
        "  aliveBindings: number,\n" +
        "  deadBindings: number,\n" +
        "  threshold: number,\n" +
        "  hasLeaks: boolean,\n" +
        "  suspects: [{\n" +
        "    elementId, elementType, bindingCount: number\n" +
        "  }],\n" +
        "  potentialLeaks: [{ type, hashCode, toString }]\n" +
        "}\n\n" +
        "Empty suspects array means no leak candidates crossed the threshold. potentialLeaks retains raw diagnostic samples for backward compatibility. " +
        "Use samplingDurationMs>=3000 for higher-confidence leak diagnostics, or set warmUp=true to automatically use the minimum recommended sampling window when one is not supplied.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, threshold: 50 }\n" +
        "- { processId: 12345, threshold: 50, samplingDurationMs: 3000 }\n" +
        "- { processId: 12345, threshold: 50, warmUp: true }")]
    public static Task<CallToolResult> FindBindingLeaks(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional minimum live-binding count that should be flagged as suspicious.")] int? threshold = null,
        [Description("Optional sampling duration in milliseconds before evaluating leak signals. Recommended: >=3000 for higher-confidence output.")] int? samplingDurationMs = null,
        [Description("When true, automatically applies the minimum recommended sampling duration if samplingDurationMs is omitted or shorter than the recommendation.")] bool warmUp = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("threshold", threshold),
            ("samplingDurationMs", samplingDurationMs),
            ("warmUp", warmUp));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<FindBindingLeaksTool>("FindBindingLeaksTool", () => new FindBindingLeaksTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "measure_element_render_time", Title = "Measure WPF Element Render Time", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to measure WPF element render time for targeted runtime performance diagnosis.\n\n" +
        PerformanceMetadata + "[Performance] Measure the render time of a WPF element in milliseconds. " +
        "Forces a re-render and measures the time taken.\n\n" +
        "USE WHEN: Identifying slow-rendering elements; profiling UI performance.\n" +
        "DO NOT USE: Repeatedly in a loop (causes performance overhead).\n\n" +
        "PERFORMANCE: This tool forces a re-render, which may briefly impact UI responsiveness.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  renderTimeMs: number,\n" +
        "  confidence: 'low',\n" +
        "  recommendedSampleCount: number,\n" +
        "  sampleGuidance: string,\n" +
        "  elementId\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> MeasureElementRenderTime(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
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

    [McpServerTool(Name = "get_visual_count", Title = "Count WPF Visual Elements", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to count WPF visual elements in a runtime subtree and detect UI complexity hot spots.\n\n" +
        PerformanceMetadata + "[Performance] Get the count of visual elements in a WPF element subtree. " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
    public static Task<CallToolResult> GetVisualCount(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
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
