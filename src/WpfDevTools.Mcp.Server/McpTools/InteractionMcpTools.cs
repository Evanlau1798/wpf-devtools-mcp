using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Interaction tools (5 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class InteractionMcpTools
{
    [McpServerTool(Name = "click_element", OpenWorld = false, Destructive = true)]
    [Description(
        "[Interaction] Simulate a mouse click on a WPF element. " +
        "Raises the full WPF click event pipeline.\n\n" +
        "USE WHEN: Testing button handlers, navigation, or click-triggered logic.\n" +
        "DO NOT USE: On disabled elements (check IsEnabled first with get_dp_value_source).\n\n" +
        "WARNING: This triggers real application logic (e.g., button handlers, navigation, data modifications).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  clicked: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"elementId required\" -> must specify which element to click\n" +
        "- \"element not found\" -> verify elementId from get_visual_tree\n" +
        "- \"element not clickable\" -> element is disabled or not a clickable type\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }\n" +
        "- { processId: 12345, elementId: \"ClearButton\" }")]
    public static Task<CallToolResult> ClickElement(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Element ID of the clickable control.")] string elementId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ClickElementTool>("ClickElementTool", () => new ClickElementTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "drag_and_drop", OpenWorld = false, Destructive = true)]
    [Description(
        "[Interaction] Simulate drag and drop between two WPF elements. " +
        "Raises DragEnter, DragOver, and Drop events on the target.\n\n" +
        "USE WHEN: Testing drag-drop functionality, reordering items, or file drop handlers.\n" +
        "DO NOT USE: Without verifying both elements exist first.\n\n" +
        "WARNING: This triggers real application logic.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  dropped: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"source not found\" -> verify sourceElementId\n" +
        "- \"target not found\" -> verify targetElementId\n" +
        "- \"sourceElementId required\" -> must specify drag source\n" +
        "- \"targetElementId required\" -> must specify drop target\n\n" +
        "Examples:\n" +
        "- { processId: 12345, sourceElementId: \"Item1\", targetElementId: \"Item2\" }")]
    public static Task<CallToolResult> DragAndDrop(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Element ID that acts as the drag source.")] string sourceElementId,
        [Description("Element ID that acts as the drop target.")] string targetElementId,
        [Description("Optional WPF data format for the drag payload, such as Text.")] string? dataFormat = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("sourceElementId", sourceElementId),
            ("targetElementId", targetElementId),
            ("dataFormat", dataFormat));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(
                "drag_and_drop",
                () => new GenericPipeTool(sessionManager, "drag_and_drop", GenericPipeTool.ExtractDragAndDropParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "scroll_to_element", OpenWorld = false, Destructive = true)]
    [Description(
        "[Interaction] Scroll a WPF element into view within its parent ScrollViewer. " +
        "Calls BringIntoView() on the element.\n\n" +
        "USE WHEN: Element is off-screen before taking screenshot or clicking; testing scroll behavior.\n" +
        "DO NOT USE: On elements not inside a ScrollViewer (has no effect).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  scrolled: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"elementId required\" -> must specify which element to scroll to\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
    public static Task<CallToolResult> ScrollToElement(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Element ID that should be brought into view.")] string elementId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ScrollToElementTool>("ScrollToElementTool", () => new ScrollToElementTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "simulate_keyboard", OpenWorld = false, Destructive = true)]
    [Description(
        "[Interaction] Simulate a keyboard key press on an element. " +
        "Key parameter uses WPF Key enum names.\n\n" +
        "USE WHEN: Testing keyboard shortcuts, Enter key submission, Tab navigation, or key event handlers.\n" +
        "DO NOT USE: For text input (use set_dp_value on Text property instead).\n\n" +
        "WARNING: This triggers real application logic.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  keyPressed: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid key\" -> key name not recognized (use WPF Key enum names)\n" +
        "- \"key required\" -> must specify which key to press\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", key: \"Enter\" }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", key: \"Tab\" }")]
    public static Task<CallToolResult> SimulateKeyboard(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("WPF Key enum name to simulate, such as Enter or Tab.")] string key,
        [Description("Optional focused element ID that should receive the key input.")] string? elementId = null,
        [Description("Optional keyboard event type: 'KeyDown' (default) or 'KeyUp'.")] string? eventType = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("key", key),
            ("eventType", eventType));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<SimulateKeyboardTool>("SimulateKeyboardTool", () => new SimulateKeyboardTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "element_screenshot", OpenWorld = false, ReadOnly = true)]
    [Description(
        "[Interaction] Capture a PNG screenshot of a specific element. " +
        "Returns base64-encoded image data. The screenshot is taken on the TARGET MACHINE running the WPF app.\n\n" +
        "USE WHEN: Visual verification needed; documenting UI state; debugging rendering issues.\n" +
        "DO NOT USE: On off-screen elements (use scroll_to_element first).\n\n" +
        "PERFORMANCE: Large elements produce large base64 strings. Prefer smaller targets in interactive STDIO sessions.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  base64Image: string,\n" +
        "  width: number,\n" +
        "  height: number,\n" +
        "  format: 'png'\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"render failed\" -> element may be collapsed or have zero size\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> ElementScreenshot(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional element ID to capture. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ElementScreenshotTool>("ElementScreenshotTool", () => new ElementScreenshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
