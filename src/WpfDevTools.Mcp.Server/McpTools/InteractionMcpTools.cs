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
    private const string InteractionMetadata = "CATEGORY: Interaction | SAFETY: Check the SDK ReadOnly and Destructive flags before invoking this tool.\n\n";
    [McpServerTool(Name = "click_element", Title = "Interact With WPF Element", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to interact with a WPF element through a runtime click path that matches user behavior.\n\n" +
        InteractionMetadata + "[Interaction] Simulate a mouse click on a WPF element. " +
        "Uses ButtonBase.OnClick() for all ButtonBase descendants (Button, ToggleButton, CheckBox, RadioButton) " +
        "which triggers ICommand execution + Click event. For TabItem, selects the tab. " +
        "Returns error for non-clickable element types.\n\n" +
        "USE WHEN: Testing button handlers, navigation, or click-triggered logic; executing ICommand via button click.\n" +
        "DO NOT USE: On disabled elements (check IsEnabled first with get_dp_value_source); on non-ButtonBase/non-TabItem elements.\n\n" +
        "SEMANTIC DIFFERENCE FROM fire_routed_event:\n" +
        "- click_element: calls OnClick() for ButtonBase descendants (includes ICommand execution + Click event); selects TabItem\n" +
        "- fire_routed_event('Click'): on ButtonBase calls OnClick() (same ICommand execution); on non-ButtonBase only fires routed event handlers\n" +
        "- For button ICommand testing, both tools work; click_element is preferred for general use\n\n" +
        "WARNING: This triggers real application logic (e.g., button handlers, navigation, data modifications).\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Use `standard` (default) for requested/effective input + observedEffect, or `compact` to keep only the core click result.\n\n" +
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
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }\n" +
        "- { processId: 12345, elementId: \"ClearButton\" }")]
    public static Task<CallToolResult> ClickElement(
        SessionManager sessionManager,
        [Description("Element ID of the clickable control.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional metadata detail mode: 'standard' (default) or 'compact'.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ClickElementTool>("ClickElementTool", () => new ClickElementTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "click_element");
    }

    [McpServerTool(Name = "get_focus_state", Title = "Inspect WPF Focus State", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to inspect the current WPF focus state across a window or scoped subtree.\n\n" +
        InteractionMetadata + "[Interaction] Get the current logical or keyboard focus snapshot for a window or element scope.\n\n" +
        "USE WHEN: Multi-window workflows, focus-sensitive interactions, or before capturing a restorable state snapshot.\n" +
        "DO NOT USE: As a persistent subscription; this is a point-in-time snapshot only.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  focusKind: 'Logical'|'Keyboard'|'None',\n" +
        "  focusedElementId: string|null,\n" +
        "  focusedElementType: string|null,\n" +
        "  windowElementId: string|null,\n" +
        "  windowTitle: string\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"SettingsDialog_1\" }")]
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
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(
                "get_focus_state",
                () => new GenericPipeTool(sessionManager, "get_focus_state")
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "focus_element", Title = "Focus WPF Element", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to move focus to a specific WPF element before keyboard-driven runtime inspection.\n\n" +
        InteractionMetadata + "[Interaction] Move logical focus to a specific WPF element.\n\n" +
        "USE WHEN: Restoring focus after a mutation sequence, or preparing a keyboard-driven workflow.\n" +
        "DO NOT USE: On elements that cannot receive focus.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  focused: boolean,\n" +
        "  focusKind: 'Logical'|'Keyboard',\n" +
        "  focusedElementId: string|null\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"elementId required\" -> must specify which element should receive focus\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SearchTextBox\" }")]
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
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(
                "focus_element",
                () => new GenericPipeTool(sessionManager, "focus_element")
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "drag_and_drop", Title = "Simulate WPF Drag And Drop", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to simulate WPF drag and drop behavior between two runtime elements.\n\n" +
        InteractionMetadata + "[Interaction] Simulate drag and drop between two WPF elements. " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345, sourceElementId: \"Item1\", targetElementId: \"Item2\" }")]
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
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(
                "drag_and_drop",
                () => new GenericPipeTool(sessionManager, "drag_and_drop", GenericPipeTool.ExtractDragAndDropParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "scroll_to_element", Title = "Scroll WPF Element Into View", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to scroll a WPF element into view before runtime screenshots or interactions.\n\n" +
        InteractionMetadata + "[Interaction] Scroll a WPF element into view within its parent ScrollViewer. " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
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
            (a, ct) => ToolCallHelper.CachedTool<ScrollToElementTool>("ScrollToElementTool", () => new ScrollToElementTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "simulate_keyboard", Title = "Simulate WPF Keyboard Input", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to simulate WPF keyboard input when runtime focus, shortcuts, or key handlers matter.\n\n" +
        InteractionMetadata + "[Interaction] Simulate a keyboard key press on an element. " +
        "Key parameter uses WPF Key enum names.\n\n" +
        "USE WHEN: Testing keyboard shortcuts, Enter key submission, Tab navigation, or key event handlers.\n" +
        "DO NOT USE: For text input (use set_dp_value on Text property instead).\n\n" +
        "WARNING: This triggers real application logic.\n\n" +
        "SEMANTIC EFFECTS: semanticEffectObserved=true when: Tab moves focus, " +
        "Enter/Space activates a Button (triggers OnClick and ICommand), " +
        "Enter/Space toggles a CheckBox, or Up/Down changes ComboBox selection. " +
        "appliedDirectEdit=true when character keys modify TextBox text.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  key,\n" +
        "  eventType,\n" +
        "  appliedDirectEdit: boolean,\n" +
        "  focusChanged: boolean,\n" +
        "  semanticEffectObserved: boolean,\n" +
        "  focusedElementIdBefore: string|null,\n" +
        "  focusedElementIdAfter: string|null\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid key\" -> key name not recognized (use WPF Key enum names)\n" +
        "- \"key required\" -> must specify which key to press\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", key: \"Enter\" }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", key: \"Tab\" }")]
    public static Task<CallToolResult> SimulateKeyboard(
        SessionManager sessionManager,
        [Description("WPF Key enum name to simulate, such as Enter or Tab.")] string key,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
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

    [McpServerTool(Name = "element_screenshot", Title = "Capture WPF Element Screenshot", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to capture a WPF element screenshot for runtime visual verification.\n\n" +
        InteractionMetadata + "[Interaction] Capture a PNG screenshot of a specific element. " +
        "Returns base64 image data by default, compact metadata when requested, or a temporary PNG file path in file mode. The screenshot is taken on the TARGET MACHINE running the WPF app.\n\n" +
        "USE WHEN: Visual verification needed; documenting UI state; debugging rendering issues.\n" +
        "DO NOT USE: On off-screen elements (use scroll_to_element first).\n\n" +
        "PERFORMANCE: Large elements produce large base64 strings. Prefer `outputMode: \"metadata\"` or `outputMode: \"file\"`, and/or `maxWidth` / `maxHeight` in interactive STDIO sessions.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  base64Image?: string,\n" +
        "  screenshotId?: string,\n" +
        "  path?: string,\n" +
        "  sha256?: string,\n" +
        "  width: number,\n" +
        "  height: number,\n" +
        "  format: 'png',\n" +
        "  byteLength: number\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid outputMode\" -> use base64, metadata, or file\n" +
        "- \"render failed\" -> element may be collapsed or have zero size\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }\n" +
        "- { processId: 12345, outputMode: \"file\", maxWidth: 512 }\n" +
        "- { processId: 12345, outputMode: \"metadata\", maxWidth: 512 }\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> ElementScreenshot(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID to capture. Omit for the root window.")] string? elementId = null,
        [Description("Optional screenshot output mode: 'base64' (default), 'metadata', or 'file'.")] string? outputMode = null,
        [Description("Optional maximum screenshot width. When provided, the image is downscaled proportionally and never upscaled.")] int? maxWidth = null,
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
            (a, ct) => ToolCallHelper.CachedTool<ElementScreenshotTool>("ElementScreenshotTool", () => new ElementScreenshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
