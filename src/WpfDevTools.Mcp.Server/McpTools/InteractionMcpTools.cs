using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Interaction tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class InteractionMcpTools
{

    [McpServerTool(Name = "click_element", Title = "Click WPF Element", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(InteractionMcpToolDescriptions.ClickElement)]
    public static Task<CallToolResult> ClickElement(
        SessionManager sessionManager,
        [Description("Element ID of the clickable control.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [AllowedValues("compact", "minimal", "verbose", "standard")]
        [Description("Optional metadata detail mode: omit or use 'compact' (default), use 'minimal' for success-only confirmation, use 'verbose' for full additive metadata, or 'standard' as a compatibility alias.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ClickElementTool>(sessionManager, "ClickElementTool", () => new ClickElementTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "click_element");
    }

    [McpServerTool(Name = "get_focus_state", Title = "Inspect WPF Focus State", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(InteractionMcpToolDescriptions.GetFocusState)]
    public static Task<CallToolResult> GetFocusState(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID used to scope focus resolution to a specific window or subtree.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, 
                "get_focus_state",
                () => new GenericPipeTool(sessionManager, "get_focus_state")
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "focus_element", Title = "Focus WPF Element", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(InteractionMcpToolDescriptions.FocusElement)]
    public static Task<CallToolResult> FocusElement(
        SessionManager sessionManager,
        [Description("Element ID that should receive focus.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, 
                "focus_element",
                () => new GenericPipeTool(sessionManager, "focus_element")
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "focus_element");
    }

    [McpServerTool(Name = "drag_and_drop", Title = "Simulate WPF Drag And Drop", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(InteractionMcpToolDescriptions.DragAndDrop)]
    public static Task<CallToolResult> DragAndDrop(
        SessionManager sessionManager,
        [Description("Element ID that acts as the drag source.")] string sourceElementId,
        [Description("Element ID that acts as the drop target.")] string targetElementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional WPF data format for the drag payload, such as Text.")] string? dataFormat = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("sourceElementId", sourceElementId),
            ("targetElementId", targetElementId),
            ("dataFormat", dataFormat));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, 
                "drag_and_drop",
                () => new GenericPipeTool(sessionManager, "drag_and_drop", GenericPipeTool.ExtractDragAndDropParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "drag_and_drop");
    }

    [McpServerTool(Name = "scroll_to_element", Title = "Scroll WPF Element Into View", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(InteractionMcpToolDescriptions.ScrollToElement)]
    public static Task<CallToolResult> ScrollToElement(
        SessionManager sessionManager,
        [Description("Element ID that should be brought into view.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ScrollToElementTool>(sessionManager, "ScrollToElementTool", () => new ScrollToElementTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "scroll_to_element");
    }

    [McpServerTool(Name = "simulate_keyboard", Title = "Simulate WPF Keyboard Input", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(InteractionMcpToolDescriptions.SimulateKeyboard)]
    public static Task<CallToolResult> SimulateKeyboard(
        SessionManager sessionManager,
        [Description("WPF Key enum name to simulate, such as Enter or Tab.")] string key,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional focused element ID that should receive the key input.")] string? elementId = null,
        [AllowedValues("KeyDown", "KeyUp")]
        [Description("Optional keyboard event type: 'KeyDown' (default) or 'KeyUp'.")] string? eventType = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("key", key),
            ("eventType", eventType));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<SimulateKeyboardTool>(sessionManager, "SimulateKeyboardTool", () => new SimulateKeyboardTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "simulate_keyboard");
    }

    [McpServerTool(Name = "element_screenshot", Title = "Capture WPF Element Screenshot", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(InteractionMcpToolDescriptions.ElementScreenshot)]
    public static Task<CallToolResult> ElementScreenshot(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID to capture. Omit for the root window.")] string? elementId = null,
        [AllowedValues("metadata", "file", "base64")]
        [Description("Optional screenshot output mode: 'metadata' (default), 'file', or explicit 'base64'.")] string? outputMode = null,
        [Range(1, int.MaxValue)]
        [Description("Optional maximum screenshot width. When provided, the image is downscaled proportionally and never upscaled.")] int? maxWidth = null,
        [Range(1, int.MaxValue)]
        [Description("Optional maximum screenshot height. When provided, the image is downscaled proportionally and never upscaled.")] int? maxHeight = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("outputMode", outputMode),
            ("maxWidth", maxWidth),
            ("maxHeight", maxHeight));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ElementScreenshotTool>(sessionManager, "ElementScreenshotTool", () => new ElementScreenshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
