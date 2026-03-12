using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

[McpServerToolType]
public static class SceneDiagnosticsMcpTools
{
    private const string SceneMetadata = "CATEGORY: Scene Diagnostics | SAFETY: These tools aggregate existing runtime diagnostics to reduce multi-call analysis flows.\n\n";

    [McpServerTool(Name = "get_state_diff", Title = "Inspect WPF Runtime State Diff", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
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
            cancellationToken);
    }
}
