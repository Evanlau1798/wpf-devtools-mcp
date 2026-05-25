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
    [McpServerTool(Name = "get_render_stats", Title = "Inspect WPF Render Stats", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(PerformanceMcpToolDescriptions.GetRenderStats)]
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
            (a, ct) => ToolCallHelper.CachedTool<GetRenderStatsTool>(sessionManager, "GetRenderStatsTool", () => new GetRenderStatsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "find_binding_leaks", Title = "Find WPF Binding Leaks", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(PerformanceMcpToolDescriptions.FindBindingLeaks)]
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
            (a, ct) => ToolCallHelper.CachedTool<FindBindingLeaksTool>(sessionManager, "FindBindingLeaksTool", () => new FindBindingLeaksTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "measure_element_render_time", Title = "Measure WPF Element Render Time", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(PerformanceMcpToolDescriptions.MeasureElementRenderTime)]
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
            (a, ct) => ToolCallHelper.CachedTool<MeasureElementRenderTimeTool>(sessionManager, "MeasureElementRenderTimeTool", () => new MeasureElementRenderTimeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_visual_count", Title = "Count WPF Visual Elements", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(PerformanceMcpToolDescriptions.GetVisualCount)]
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
            (a, ct) => ToolCallHelper.CachedTool<GetVisualCountTool>(sessionManager, "GetVisualCountTool", () => new GetVisualCountTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
