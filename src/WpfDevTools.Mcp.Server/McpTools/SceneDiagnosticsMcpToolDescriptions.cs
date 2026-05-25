namespace WpfDevTools.Mcp.Server.McpTools;

internal static class SceneDiagnosticsMcpToolDescriptions
{
    private const string SceneMetadata = "CATEGORY: Scene Diagnostics\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetStateDiff =
        "Use this tool to compare a previously captured WPF runtime state snapshot with the current runtime state.\n\n" +
        SceneMetadata +
        "[Scene] Compute semantic before/after differences for tracked DependencyProperty values, ViewModel properties, focus, binding errors, and validation errors.\n\n" +
        "USE WHEN: After click_element, execute_command, modify_viewmodel, or manual debugging steps when you need to know what changed.\n" +
        "DO NOT USE: As a replacement for capture_state_snapshot; you must capture a snapshot first.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  snapshotId: string,\n" +
        "  trigger: string|null,\n" +
        "  durationMs: number,\n" +
        "  propertyChanges: [],\n" +
        "  viewModelChanges: [],\n" +
        "  newBindingErrors: [],\n" +
        "  resolvedBindingErrors: [],\n" +
        "  validationChanges: [],\n" +
        "  focusChange: object|null\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"snapshotId\" -> capture_state_snapshot first or verify the snapshotId before retrying\n" +
        "- \"not connected\" -> reconnect before diffing the stored snapshot\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"snapshotId\": \"snapshot_abc\" }\n" +
        "- { \"processId\": 12345, \"snapshotId\": \"snapshot_abc\", \"trigger\": \"click_element(SaveButton)\" }";

    public const string GetElementSnapshot =
        "Use this tool to aggregate the most common WPF diagnostics for a single runtime element into one token-efficient snapshot.\n\n" +
        SceneMetadata +
        "[Scene] Gather element identity, selected DependencyProperty values, bindings, validation errors, style summary, layout summary, and DataContext type in one call.\n\n" +
        "USE WHEN: Before falling back to screenshots, or when you need one element-centric snapshot instead of multiple diagnostic calls.\n" +
        "PROPERTY PROBES: The default snapshot includes a stable baseline property set. Provide `includeProperties` to append extra DependencyProperty probes such as `IsChecked` or `SelectedIndex` without replacing the defaults.\n" +
        "DO NOT USE: As a full-tree replacement; use get_visual_tree/get_logical_tree for broad structural inspection.\n\n" +
        ToolDescriptionFragments.ContractGuidance +
        "RESPONSE FIELDS: elementId, elementType, elementName, dataContextType, properties, bindings, validationErrors, style, and layout.\n" +
        "REQUEST OPTIONS: includeProperties appends extra DependencyProperty probes to the default snapshot set.\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"TextBox_42\" }\n" +
        "- { \"elementId\": \"SaveButton_7\" }";

    public const string DiagnoseVisibility =
        "Use this tool to explain why a WPF runtime element is or is not user-visible without relying on screenshots.\n\n" +
        SceneMetadata +
        "[Scene] Diagnose visibility blockers such as element or ancestor Visibility, zero Opacity, zero layout size, and clipping.\n\n" +
        "USE WHEN: An element exists in the tree but does not appear on screen, or when you want a structured replacement for screenshot-based visibility debugging.\n" +
        "DO NOT USE: As a generic tree browser; use get_visual_tree/get_logical_tree for structure exploration.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  elementId: string,\n" +
        "  isUserVisible: boolean,\n" +
        "  checks: [],\n" +
        "  rootCause: string|null,\n" +
        "  suggestedFix: string|null\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"elementId\" -> provide a runtime elementId from find_elements / get_visual_tree\n" +
        "- \"not connected\" -> reconnect before diagnosing visibility\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"Button_12\" }\n" +
        "- { \"elementId\": \"HiddenByAncestorText_4\" }";

    public const string GetInteractionReadiness =
        "Use this tool to determine whether a WPF runtime element is currently ready for interaction.\n\n" +
        SceneMetadata +
        "[Scene] Aggregate enabled state, visibility, opacity, hit testing, layout size, and ButtonBase ICommand.CanExecute into one interaction readiness verdict.\n\n" +
        "USE WHEN: Before click_element or simulate_keyboard when you need to know whether the target is interactable right now.\n" +
        "DO NOT USE: As a replacement for diagnose_visibility when the question is specifically why something is not visible.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  elementId: string,\n" +
        "  interactionType: string,\n" +
        "  isReady: boolean,\n" +
        "  blockers: [],\n" +
        "  commandReadiness: { hasCommand: boolean, commandName: string|null, commandNameSource: string, canExecute: boolean|null, sourceElementId: string, commandParameterKind: string, riskNotes: [] },\n" +
        "  elementState: object\n" +
        "}\n\n" +
        "COMMAND READINESS: commandReadiness is redacted and does not include command parameter values or arbitrary ViewModel values. Risk notes include CommandParameterValueRedacted when a parameter exists.\n\n" +
        "ERRORS:\n" +
        "- \"elementId\" -> provide a runtime elementId from find_elements / get_visual_tree\n" +
        "- \"not connected\" -> reconnect before inspecting readiness\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton_7\" }\n" +
        "- { \"elementId\": \"FocusActionButton_3\", \"interactionType\": \"Click\" }";

    public const string GetUiSummary =
        "Use this tool to get a token-efficient semantic summary of a WPF window or subtree without relying on screenshots.\n\n" +
        SceneMetadata +
        "[Scene] Traverse a WPF runtime subtree, suppress layout-only wrappers, and return a compact semantic overview of user-facing controls.\n\n" +
        "USE WHEN: You need fast screen context for an unfamiliar area before drilling into a specific element. For agent workflows, prefer depthMode='semantic' so layout-only wrapper levels do not consume the depth budget, and prefer summaryOnly=true when you only need summaryText without the node table.\n" +
        "DO NOT USE: As a replacement for full tree inspection when exact structure matters.\n\n" +
        ToolDescriptionFragments.ContractGuidance +
        "RESPONSE FIELDS: rootElementId, rootElementType, rootElementName, depth, semanticNodeCount, summaryText, and nodes.\n" +
        "REQUEST OPTIONS: elementId scopes the subtree; depth and depthMode='semantic' shape traversal; summaryOnly omits the node table.\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"depthMode\": \"semantic\" }\n" +
        "- { \"elementId\": \"BasicControlsStackPanel_4\", \"depth\": 4, \"depthMode\": \"semantic\" }\n" +
        "- { \"processId\": 12345, \"depthMode\": \"semantic\", \"summaryOnly\": true }";

    public const string GetFormSummary =
        "Use this tool to summarize the current state of a WPF form subtree in one call.\n\n" +
        SceneMetadata +
        "[Scene] Aggregate common input controls, nearby labels, current values, validation errors, command readiness, and overall form submittability.\n\n" +
        "USE WHEN: You want a single triage call for form-style layouts before validating or clicking Save/Submit. By default, framework-internal template controls such as RepeatButton or DataGrid headers are filtered out unless you explicitly set includeFramework=true.\n" +
        "DO NOT USE: For arbitrary non-form regions with no input or action controls.\n\n" +
        ToolDescriptionFragments.ContractGuidance +
        "RESPONSE FIELDS: formScope, scopeVisibility, isCurrentlyVisible, inputs, commands, summary, and nested summary.totalInputs/summary.emptyInputs/summary.errorCount/summary.validationSubmittable/summary.interactionSubmittable/summary.isSubmittable.\n" +
        "REQUEST OPTIONS: includeFramework keeps framework-internal template controls in the summary.\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"BasicControlsStackPanel_4\" }\n" +
        "- { \"elementId\": \"ProfileForm_2\" }\n" +
        "- { \"processId\": 12345, \"includeFramework\": true }";
}
