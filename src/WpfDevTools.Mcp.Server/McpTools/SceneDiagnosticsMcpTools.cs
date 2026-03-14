using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

[McpServerToolType]
public static class SceneDiagnosticsMcpTools
{
    private const string SceneMetadata = "CATEGORY: Scene Diagnostics | SAFETY: These tools aggregate existing runtime diagnostics to reduce multi-call analysis flows.\n\n";
    private const string RuntimeNavigationGuidance = "FOLLOW-UP GUIDANCE: Successful responses may include runtime-computed `navigation.recommended` plus compatibility field `nextSteps`; prefer `navigation.recommended` when present instead of ad hoc tool guessing.\n\n";

    [McpServerTool(Name = "get_state_diff", Title = "Inspect WPF Runtime State Diff", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to compare a previously captured WPF runtime state snapshot with the current runtime state.\n\n" +
        SceneMetadata +
        "[Scene] Compute semantic before/after differences for tracked DependencyProperty values, ViewModel properties, focus, binding errors, and validation errors.\n\n" +
        "USE WHEN: After click_element, execute_command, modify_viewmodel, or manual debugging steps when you need to know what changed.\n" +
        "DO NOT USE: As a replacement for capture_state_snapshot; you must capture a snapshot first.\n\n" +
        RuntimeNavigationGuidance +
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
        "- { processId: 12345, snapshotId: \"snapshot_abc\" }\n" +
        "- { processId: 12345, snapshotId: \"snapshot_abc\", trigger: \"click_element(SaveButton)\" }")]
    public static Task<CallToolResult> GetStateDiff(
        SessionManager sessionManager,
        [Description("Snapshot ID returned by capture_state_snapshot.")] string snapshotId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional human-readable description of the action that happened after the snapshot was captured.")] string? trigger = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("snapshotId", snapshotId),
            ("trigger", trigger));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetStateDiffTool>(
                nameof(GetStateDiffTool),
                () => new GetStateDiffTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_state_diff");
    }

    [McpServerTool(Name = "get_element_snapshot", Title = "Inspect WPF Element Snapshot", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to aggregate the most common WPF diagnostics for a single runtime element into one token-efficient snapshot.\n\n" +
        SceneMetadata +
        "[Scene] Gather element identity, selected DependencyProperty values, bindings, validation errors, style summary, layout summary, and DataContext type in one call.\n\n" +
        "USE WHEN: Before falling back to screenshots, or when you need one element-centric snapshot instead of multiple diagnostic calls.\n" +
        "DO NOT USE: As a full-tree replacement; use get_visual_tree/get_logical_tree for broad structural inspection.\n\n" +
        RuntimeNavigationGuidance +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  elementId: string,\n" +
        "  elementType: string,\n" +
        "  elementName: string|null,\n" +
        "  dataContextType: string|null,\n" +
        "  properties: object,\n" +
        "  bindings: [],\n" +
        "  validationErrors: [],\n" +
        "  style: object,\n" +
        "  layout: object\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"elementId\" -> provide a runtime elementId from find_elements / get_visual_tree\n" +
        "- \"not connected\" -> reconnect before requesting an aggregated snapshot\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"TextBox_42\" }\n" +
        "- { elementId: \"SaveButton_7\" }")]
    public static Task<CallToolResult> GetElementSnapshot(
        SessionManager sessionManager,
        [Description("Runtime element ID to inspect.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetElementSnapshotTool>(
                nameof(GetElementSnapshotTool),
                () => new GetElementSnapshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_element_snapshot");
    }

    [McpServerTool(Name = "diagnose_visibility", Title = "Diagnose WPF Element Visibility", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to explain why a WPF runtime element is or is not user-visible without relying on screenshots.\n\n" +
        SceneMetadata +
        "[Scene] Diagnose visibility blockers such as element or ancestor Visibility, zero Opacity, zero layout size, and clipping.\n\n" +
        "USE WHEN: An element exists in the tree but does not appear on screen, or when you want a structured replacement for screenshot-based visibility debugging.\n" +
        "DO NOT USE: As a generic tree browser; use get_visual_tree/get_logical_tree for structure exploration.\n\n" +
        RuntimeNavigationGuidance +
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
        "- { processId: 12345, elementId: \"Button_12\" }\n" +
        "- { elementId: \"HiddenByAncestorText_4\" }")]
    public static Task<CallToolResult> DiagnoseVisibility(
        SessionManager sessionManager,
        [Description("Runtime element ID to inspect.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<DiagnoseVisibilityTool>(
                nameof(DiagnoseVisibilityTool),
                () => new DiagnoseVisibilityTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "diagnose_visibility");
    }

    [McpServerTool(Name = "get_interaction_readiness", Title = "Inspect WPF Interaction Readiness", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to determine whether a WPF runtime element is currently ready for interaction.\n\n" +
        SceneMetadata +
        "[Scene] Aggregate enabled state, visibility, opacity, hit testing, layout size, and ButtonBase ICommand.CanExecute into one interaction readiness verdict.\n\n" +
        "USE WHEN: Before click_element or simulate_keyboard when you need to know whether the target is interactable right now.\n" +
        "DO NOT USE: As a replacement for diagnose_visibility when the question is specifically why something is not visible.\n\n" +
        RuntimeNavigationGuidance +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  elementId: string,\n" +
        "  interactionType: string,\n" +
        "  isReady: boolean,\n" +
        "  blockers: [],\n" +
        "  elementState: object\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"elementId\" -> provide a runtime elementId from find_elements / get_visual_tree\n" +
        "- \"not connected\" -> reconnect before inspecting readiness\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton_7\" }\n" +
        "- { elementId: \"FocusActionButton_3\", interactionType: \"Click\" }")]
    public static Task<CallToolResult> GetInteractionReadiness(
        SessionManager sessionManager,
        [Description("Runtime element ID to inspect.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional interaction type label. Defaults to Click.")] string interactionType = "Click",
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("interactionType", interactionType));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetInteractionReadinessTool>(
                nameof(GetInteractionReadinessTool),
                () => new GetInteractionReadinessTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_interaction_readiness");
    }

    [McpServerTool(Name = "get_ui_summary", Title = "Summarize WPF UI Semantics", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to get a token-efficient semantic summary of a WPF window or subtree without relying on screenshots.\n\n" +
        SceneMetadata +
        "[Scene] Traverse a WPF runtime subtree, suppress layout-only wrappers, and return a compact semantic overview of user-facing controls.\n\n" +
        "USE WHEN: You need fast screen context for an unfamiliar area before drilling into a specific element. For agent workflows, prefer depthMode='semantic' so layout-only wrapper levels do not consume the depth budget.\n" +
        "DO NOT USE: As a replacement for full tree inspection when exact structure matters.\n\n" +
        RuntimeNavigationGuidance +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  rootElementId: string,\n" +
        "  rootElementType: string,\n" +
        "  rootElementName: string|null,\n" +
        "  depth: number,\n" +
        "  semanticNodeCount: number,\n" +
        "  summaryText: string,\n" +
        "  nodes: []\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"elementId\" -> provide a runtime elementId from find_elements / get_visual_tree, or omit it to summarize the root window\n" +
        "- \"not connected\" -> reconnect before requesting a semantic UI summary\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, depthMode: \"semantic\" }\n" +
        "- { elementId: \"BasicControlsStackPanel_4\", depth: 4, depthMode: \"semantic\" }")]
    public static Task<CallToolResult> GetUiSummary(
        SessionManager sessionManager,
        [Description("Optional runtime element ID to scope the semantic summary. Omit to summarize the root window.")] string? elementId = null,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional maximum visual depth to summarize. Omit to use the default semantic summary depth budget.")] int? depth = null,
        [Description("Optional depth accounting mode: 'semantic' (default) skips layout-only wrapper levels when budgeting depth, while 'visual' counts every traversed level.")] string? depthMode = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth),
            ("depthMode", depthMode));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetUiSummaryTool>(
                nameof(GetUiSummaryTool),
                () => new GetUiSummaryTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_ui_summary");
    }

    [McpServerTool(Name = "get_form_summary", Title = "Summarize WPF Form State", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to summarize the current state of a WPF form subtree in one call.\n\n" +
        SceneMetadata +
        "[Scene] Aggregate common input controls, nearby labels, current values, validation errors, command readiness, and overall form submittability.\n\n" +
        "USE WHEN: You want a single triage call for form-style layouts before validating or clicking Save/Submit.\n" +
        "DO NOT USE: For arbitrary non-form regions with no input or action controls.\n\n" +
        RuntimeNavigationGuidance +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  formScope: string,\n" +
        "  inputs: [],\n" +
        "  commands: [],\n" +
        "  summary: { totalInputs, emptyInputs, errorCount, isSubmittable }\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"elementId\" -> provide a runtime elementId from find_elements / get_visual_tree, or omit it to summarize the root window form state\n" +
        "- \"not connected\" -> reconnect before requesting a form summary\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"BasicControlsStackPanel_4\" }\n" +
        "- { elementId: \"ProfileForm_2\" }")]
    public static Task<CallToolResult> GetFormSummary(
        SessionManager sessionManager,
        [Description("Optional runtime element ID to scope the form summary. Omit to use the root window.")] string? elementId = null,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetFormSummaryTool>(
                nameof(GetFormSummaryTool),
                () => new GetFormSummaryTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_form_summary");
    }
}
