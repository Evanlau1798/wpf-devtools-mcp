namespace WpfDevTools.Mcp.Server.McpTools;

internal static class LayoutMcpToolDescriptions
{
    private const string LayoutMetadata = "CATEGORY: Layout\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetLayoutInfo =
        "Use this tool to inspect WPF layout measurements and runtime positioning for an element.\n\n" +
        LayoutMetadata + "[Layout] Get layout information of a WPF element. Returns: actualWidth, actualHeight, " +
        "desiredSize, renderSize, margin, padding, horizontalAlignment, verticalAlignment, " +
        "position relative to parent and window.\n\n" +
        "USE WHEN: Element has wrong size, position, or alignment; debugging layout issues.\n" +
        "BATCH MODE: Provide `elementIds` to inspect multiple elements in one call. Single-target responses keep the original shape; batch responses return `results` with per-item `elementId` correlation.\n" +
        "DO NOT USE: For clipping issues (use get_clipping_info instead).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - actualWidth, actualHeight, desiredWidth, desiredHeight,\n" +
        "  - margin: { left, top, right, bottom },\n" +
        "  - padding: { left, top, right, bottom },\n" +
        "  - horizontalAlignment, verticalAlignment,\n" +
        "  - positionInParent: { x, y }, positionInWindow: { x, y }\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }\n" +
        "- { \"processId\": 12345 }";

    public const string GetClippingInfo =
        "Use this tool to inspect WPF clipping and overflow when rendered content appears cut off.\n\n" +
        LayoutMetadata + "[Layout] Get clipping information of a WPF element. Returns whether the element " +
        "is clipped by any ancestor, the clip bounds, and how much content overflows.\n\n" +
        "USE WHEN: Element appears cut off or partially hidden; debugging ScrollViewer issues.\n" +
        "DO NOT USE: For general layout info (use get_layout_info instead).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - isClipped: boolean,\n" +
        "  - clipBounds: { x, y, width, height },\n" +
        "  - overflowAmount: { left, top, right, bottom }\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"elementId required\" -> must specify which element to check\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\" }";

    public const string HighlightElement =
        "Use this tool to highlight a WPF element so runtime inspection targets stay unambiguous.\n\n" +
        LayoutMetadata + "[Layout] Visually highlight an element with a colored border overlay. " +
        "Useful for confirming you have the right element. Color accepts WPF color names " +
        "('Red', 'Blue', 'Yellow') or hex. Auto-removes after duration.\n\n" +
        "USE WHEN: Verifying element identification; showing users which element you're inspecting.\n" +
        "DO NOT USE: On collapsed or zero-size elements (won't be visible).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - highlighted: boolean\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid color\" -> use WPF color names or hex format\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"color\": \"Red\", \"duration\": 3000 }";

    public const string InvalidateLayout =
        "Use this tool to invalidate WPF layout when runtime measurements need to be recomputed immediately.\n\n" +
        LayoutMetadata + "[Layout] Force layout invalidation on a WPF element, causing it to re-measure " +
        "and re-arrange. Use after modifying properties that affect layout to force an immediate update.\n\n" +
        "USE WHEN: Layout doesn't update after property changes; testing layout behavior.\n" +
        "DO NOT USE: Repeatedly in a loop (causes performance issues).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - invalidated: boolean\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\" }";
}