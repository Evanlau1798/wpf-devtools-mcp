using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Layout tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class LayoutMcpTools
{
    [McpServerTool(Name = "get_layout_info", Title = "Inspect WPF Layout Info", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(LayoutMcpToolDescriptions.GetLayoutInfo)]
    public static Task<CallToolResult> GetLayoutInfo(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose layout metrics should be returned. Omit for the root window.")] string? elementId = null,
        [Description("Optional list of element IDs for batch inspection. Use either elementId or elementIds, not both.")] string[]? elementIds = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetLayoutInfoTool>(sessionManager, "GetLayoutInfoTool", () => new GetLayoutInfoTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_clipping_info", Title = "Inspect WPF Clipping Info", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(LayoutMcpToolDescriptions.GetClippingInfo)]
    public static Task<CallToolResult> GetClippingInfo(
        SessionManager sessionManager,
        [Description("Optional element ID whose clipping state should be analyzed. Use either elementId or elementIds.")] string? elementId = null,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional list of element IDs for batch clipping inspection. Use either elementId or elementIds, not both.")] string[]? elementIds = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetClippingInfoTool>(sessionManager, "GetClippingInfoTool", () => new GetClippingInfoTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "highlight_element", Title = "Highlight WPF Element", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(LayoutMcpToolDescriptions.HighlightElement)]
    public static Task<CallToolResult> HighlightElement(
        SessionManager sessionManager,
        [Description("Required element ID to highlight.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional WPF color name or hex color string for the highlight overlay.")] string? color = null,
        [Description("Optional highlight duration in milliseconds before the overlay is removed.")] int? duration = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("color", color),
            ("duration", duration));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, 
                "highlight_element",
                () => new GenericPipeTool(sessionManager, "highlight_element", GenericPipeTool.ExtractHighlightElementParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "invalidate_layout", Title = "Invalidate WPF Layout", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(LayoutMcpToolDescriptions.InvalidateLayout)]
    public static Task<CallToolResult> InvalidateLayout(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose layout should be invalidated. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<InvalidateLayoutTool>(sessionManager, "InvalidateLayoutTool", () => new InvalidateLayoutTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "invalidate_layout");
    }
}
