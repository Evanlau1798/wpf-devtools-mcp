namespace WpfDevTools.Mcp.Server.McpTools;

internal static class InteractionMcpToolDescriptions
{
    private const string InteractionMetadata = "CATEGORY: Interaction\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string ClickElement =
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
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep only the core click result, use `minimal` for the most concise success confirmation, or use `verbose` for requested/effective input + observedEffect; legacy `standard` remains accepted as a compatibility alias.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - clicked: boolean\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"elementId required\" -> must specify which element to click\n" +
        "- \"element not found\" -> verify elementId from get_visual_tree\n" +
        "- \"element not clickable\" -> element is disabled or not a clickable type\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"ClearButton\" }";

    public const string GetFocusState =
        "Use this tool to inspect the current WPF focus state across a window or scoped subtree.\n\n" +
        InteractionMetadata + "[Interaction] Get the current logical or keyboard focus snapshot for a window or element scope.\n\n" +
        "USE WHEN: Multi-window workflows, focus-sensitive interactions, or before capturing a restorable state snapshot.\n" +
        "DO NOT USE: As a persistent subscription; this is a point-in-time snapshot only.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - focusKind: 'Logical'|'Keyboard'|'None',\n" +
        "  - focusedElementId: string|null,\n" +
        "  - focusedElementType: string|null,\n" +
        "  - windowElementId: string|null,\n" +
        "  - windowTitle: string\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SettingsDialog_1\" }";

    public const string FocusElement =
        "Use this tool to move focus to a specific WPF element before keyboard-driven runtime inspection.\n\n" +
        InteractionMetadata + "[Interaction] Move logical focus to a specific WPF element.\n\n" +
        "USE WHEN: Restoring focus after a mutation sequence, or preparing a keyboard-driven workflow.\n" +
        "TARGET SELECTION: Use get_ui_summary or get_form_summary first, then get_interaction_readiness when uncertain. Choose a visible, enabled, focusable control from the active rendered visual tree; if one candidate returns ElementNotLoaded or cannot receive keyboard focus, retry another loaded focusable target before reporting a product limitation.\n" +
        "DO NOT USE: On elements that cannot receive focus.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - focused: boolean,\n" +
        "  - focusKind: 'Logical'|'Keyboard',\n" +
        "  - focusedElementId: string|null\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"elementId required\" -> must specify which element should receive focus\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SearchTextBox\" }";

    public const string DragAndDrop =
        "Use this tool to simulate WPF drag and drop behavior between two runtime elements.\n\n" +
        InteractionMetadata + "[Interaction] Simulate drag and drop between two WPF elements. " +
        "Raises DragEnter, DragOver, and Drop events on the target.\n\n" +
        "USE WHEN: Testing drag-drop functionality, reordering items, or file drop handlers.\n" +
        "DO NOT USE: Without verifying both elements exist first.\n\n" +
        "WARNING: This triggers real application logic.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - targetHandlerHints: {\n" +
        "    - targetAllowsDrop: boolean,\n" +
        "    - hasDropHandler: boolean|null,\n" +
        "    - hasDragOverHandler: boolean|null,\n" +
        "    - hasAnyDropOrDragOverHandler: boolean|null,\n" +
        "    - inspectionSupported: boolean,\n" +
        "    - mayBeIncomplete: boolean\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"source not found\" -> verify sourceElementId\n" +
        "- \"target not found\" -> verify targetElementId\n" +
        "- \"sourceElementId required\" -> must specify drag source\n" +
        "- \"targetElementId required\" -> must specify drop target\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"sourceElementId\": \"Item1\", \"targetElementId\": \"Item2\" }";

    public const string ScrollToElement =
        "Use this tool to scroll a WPF element into view before runtime screenshots or interactions.\n\n" +
        InteractionMetadata + "[Interaction] Scroll a WPF element into view within its parent ScrollViewer. " +
        "Calls BringIntoView() on the element.\n\n" +
        "USE WHEN: Element is off-screen before taking screenshot or clicking; testing scroll behavior.\n" +
        "DO NOT USE: On elements not inside a ScrollViewer (has no effect).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - scrolled: boolean\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"elementId required\" -> must specify which element to scroll to\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\" }";

    public const string SimulateKeyboard =
        "Use this tool to simulate WPF keyboard input when runtime focus, shortcuts, or key handlers matter.\n\n" +
        InteractionMetadata + "[Interaction] Simulate a keyboard key press on an element. " +
        "Key parameter uses WPF Key enum names.\n\n" +
        "USE WHEN: Testing keyboard shortcuts, Enter key submission, Tab navigation, or key event handlers.\n" +
        "TARGET SELECTION: Use get_focus_state and focus_element only on visible, enabled, focusable controls in the active rendered visual tree. Use get_interaction_readiness for uncertain targets, and retry another loaded focusable element before treating a real-project keyboard workflow as unsupported.\n" +
        "DO NOT USE: For text input (use set_dp_value on Text property instead).\n\n" +
        "WARNING: This triggers real application logic.\n\n" +
        "SEMANTIC EFFECTS: semanticEffectObserved=true when: Tab moves focus, " +
        "Enter/Space activates a Button (triggers OnClick and ICommand), " +
        "Enter/Space toggles a CheckBox, or Up/Down changes ComboBox selection. " +
        "appliedDirectEdit=true when character keys modify TextBox text.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - key,\n" +
        "  - eventType,\n" +
        "  - appliedDirectEdit: boolean,\n" +
        "  - focusChanged: boolean,\n" +
        "  - semanticEffectObserved: boolean,\n" +
        "  - focusedElementIdBefore: string|null,\n" +
        "  - focusedElementIdAfter: string|null\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid key\" -> key name not recognized (use WPF Key enum names)\n" +
        "- \"key required\" -> must specify which key to press\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"key\": \"Enter\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"key\": \"Tab\" }";

    public const string ElementScreenshot =
        "Use this tool to capture a WPF element screenshot for runtime visual verification.\n\n" +
        InteractionMetadata + "[Interaction] Capture a PNG screenshot of a specific element. " +
        "Returns compact metadata by default, small inline base64 image data when explicitly requested, or a retained PNG resource URI in file mode. The screenshot is taken on the TARGET MACHINE running the WPF app.\n\n" +
        "USE WHEN: Visual verification needed; documenting UI state; debugging rendering issues.\n" +
        "DO NOT USE: As a first-pass scene exploration tool (prefer get_ui_summary or get_element_snapshot), or on off-screen elements (use scroll_to_element first).\n\n" +
        "PRIVACY: The MCP screenshot policy gate must be enabled. Use `outputMode: \"file\"` for larger pixel captures; it returns a session-scoped `resourceUri`, redacts local paths, and creates an MCP server-owned retained screenshot resource under a server-issued lease root. `SessionManager` expires it after 24 hours, caps it at 100 resources per MCP server session, deletes retained PNG files when evicted or expired, and purges them on disconnect or server session manager disposal. This lifecycle is owned by `SessionManager`, not by the Inspector default screenshot cache. Inline `base64` is capped for small images only.\n" +
        "PERFORMANCE: The default `metadata` mode does not render or return PNG bytes; metadata mode does not return `screenshotId`, `resourceUri`, or a `wpf://screenshots/{screenshotId}` handle. Metadata responses include a nextSteps entry for `outputMode: \"file\"` when pixel evidence is required. Use `outputMode: \"file\"` or explicit `outputMode: \"base64\"` plus `maxWidth` / `maxHeight` when pixels are required.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - base64Image (optional): string,\n" +
        "  - screenshotId (optional): string,\n" +
        "  - resourceUri (optional): string,\n" +
        "  - fileName (optional): string,\n" +
        "  - expiresAtUtc (optional): string,\n" +
        "  - localPathRedacted (optional): boolean,\n" +
        "  - sha256 (optional): string,\n" +
        "  - width: number,\n" +
        "  - height: number,\n" +
        "  - format: 'png',\n" +
        "  - rendered: boolean,\n" +
        "  - byteLength: number,\n" +
        "  - nextSteps (metadata mode): file-mode screenshot follow-up for pixel evidence\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid outputMode\" -> use base64, metadata, or file\n" +
        "- \"render failed\" -> element may be collapsed or have zero size\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"outputMode\": \"base64\" }\n" +
        "- { \"processId\": 12345, \"outputMode\": \"file\", \"maxWidth\": 512 }\n" +
        "- { \"processId\": 12345, \"outputMode\": \"metadata\", \"maxWidth\": 512 }\n" +
        "- { \"processId\": 12345 }";
}
