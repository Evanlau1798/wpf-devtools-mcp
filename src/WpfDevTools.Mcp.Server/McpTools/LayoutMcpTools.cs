using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

#pragma warning disable CS1591 // McpTools use [Description] attribute instead of XML doc comments

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Layout tools (4 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class LayoutMcpTools
{
    [McpServerTool(Name = "get_layout_info", ReadOnly = true)]
    [Description(
        "[Layout] Get layout information of a WPF element. Returns: actualWidth, actualHeight, " +
        "desiredSize, renderSize, margin, padding, horizontalAlignment, verticalAlignment, " +
        "position relative to parent and window.\n\n" +
        "USE WHEN: Element has wrong size, position, or alignment; debugging layout issues.\n" +
        "DO NOT USE: For clipping issues (use get_clipping_info instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  actualWidth, actualHeight, desiredWidth, desiredHeight,\n" +
        "  margin: { left, top, right, bottom },\n" +
        "  padding: { left, top, right, bottom },\n" +
        "  horizontalAlignment, verticalAlignment,\n" +
        "  positionInParent: { x, y }, positionInWindow: { x, y }\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> GetLayoutInfo(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetLayoutInfoTool>("GetLayoutInfoTool", () => new GetLayoutInfoTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_clipping_info", ReadOnly = true)]
    [Description(
        "[Layout] Get clipping information of a WPF element. Returns whether the element " +
        "is clipped by any ancestor, the clip bounds, and how much content overflows.\n\n" +
        "USE WHEN: Element appears cut off or partially hidden; debugging ScrollViewer issues.\n" +
        "DO NOT USE: For general layout info (use get_layout_info instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  isClipped: boolean,\n" +
        "  clipBounds: { x, y, width, height },\n" +
        "  overflowAmount: { left, top, right, bottom }\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"elementId required\" -> must specify which element to check\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
    public static Task<CallToolResult> GetClippingInfo(
        SessionManager sessionManager,
        int processId,
        string elementId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetClippingInfoTool>("GetClippingInfoTool", () => new GetClippingInfoTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "highlight_element", Destructive = true)]
    [Description(
        "[Layout] Visually highlight an element with a colored border overlay. " +
        "Useful for confirming you have the right element. Color accepts WPF color names " +
        "('Red', 'Blue', 'Yellow') or hex. Auto-removes after duration.\n\n" +
        "USE WHEN: Verifying element identification; showing users which element you're inspecting.\n" +
        "DO NOT USE: On collapsed or zero-size elements (won't be visible).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  highlighted: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid color\" -> use WPF color names or hex format\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", color: \"Red\", duration: 3000 }")]
    public static Task<CallToolResult> HighlightElement(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        string? color = null,
        int? duration = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("color", color),
            ("duration", duration));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(
                "highlight_element",
                () => new GenericPipeTool(sessionManager, "highlight_element", GenericPipeTool.ExtractHighlightElementParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "invalidate_layout", Destructive = true)]
    [Description(
        "[Layout] Force layout invalidation on a WPF element, causing it to re-measure " +
        "and re-arrange. Use after modifying properties that affect layout to force an immediate update.\n\n" +
        "USE WHEN: Layout doesn't update after property changes; testing layout behavior.\n" +
        "DO NOT USE: Repeatedly in a loop (causes performance issues).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  invalidated: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
    public static Task<CallToolResult> InvalidateLayout(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<InvalidateLayoutTool>("InvalidateLayoutTool", () => new InvalidateLayoutTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
