using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for State Snapshot tools.
/// </summary>
[McpServerToolType]
public static class StateMcpTools
{
    private const string StateMetadata = "CATEGORY: State\n\n";

    [McpServerTool(Name = "capture_state_snapshot", Title = "Capture WPF Runtime State Snapshot", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to capture a WPF runtime state snapshot before mutations or multi-step debugging.\n\n" +
        StateMetadata + "[State] Capture a restorable runtime snapshot for a connected WPF process.\n\n" +
        "USE WHEN: Before mutation-heavy debugging, demos, or regression flows where rollback matters.\n" +
        "DO NOT USE: As durable persistence; snapshots are in-memory and session-scoped only.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  snapshotId: string,\n" +
        "  snapshotSummary: { dependencyPropertyCount, viewModelPropertyCount, capturedFocus }\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"propertyNames / viewModelPropertyNames / includeFocus required\" -> choose at least one capture dimension\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyNames: [\"IsEnabled\"] }\n" +
        "- { processId: 12345, elementId: \"EditorPanel\", viewModelPropertyNames: [\"Name\"], includeFocus: true }")]
    public static Task<CallToolResult> CaptureStateSnapshot(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose state should be captured. Omit for the root window.")] string? elementId = null,
        [Description("Optional DependencyProperty names to capture as restorable local-value state. Binding-backed expressions are captured with same-session restore handles when possible; non-Binding expressions remain skipped capability boundaries.")] string[]? propertyNames = null,
        [Description("Optional ViewModel property names to capture from the current DataContext.")] string[]? viewModelPropertyNames = null,
        [Description("When true, also capture the current logical/keyboard focus snapshot.")] bool includeFocus = false,
        [Description("Optional human-friendly label for the snapshot.")] string? snapshotName = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyNames", propertyNames),
            ("viewModelPropertyNames", viewModelPropertyNames),
            ("includeFocus", includeFocus),
            ("snapshotName", snapshotName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<CaptureStateSnapshotTool>(
                nameof(CaptureStateSnapshotTool),
                () => new CaptureStateSnapshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "capture_state_snapshot");
    }

    [McpServerTool(Name = "restore_state_snapshot", Title = "Restore WPF Runtime State Snapshot", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to restore a WPF runtime state snapshot after temporary debugging changes.\n\n" +
        StateMetadata + "[State] Restore a previously captured in-memory runtime snapshot.\n\n" +
        "USE WHEN: Rolling back temporary DependencyProperty, ViewModel, or focus changes in the same session.\n" +
        "DO NOT USE: Across disconnected sessions or application restarts.\n\n" +
        "EXPRESSION ROLLBACK: Binding-backed DependencyProperty expressions captured in the same session can be restored. When a two-way source property also needs to return to its baseline value, capture that ViewModel property in the same snapshot. Non-Binding expressions are still surfaced through skippedDependencyProperties with explicit reasons.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  restoredDependencyPropertyCount: number,\n" +
        "  restoredDependencyProperties: [{ propertyName, verified: boolean, expectedValue, currentValue, expectedIsExpression, currentIsExpression, verificationSkippedReason }],\n" +
        "  skippedDependencyPropertyCount: number,\n" +
        "  skippedDependencyProperties: [{ propertyName, reason, restoreDisposition, verified: boolean, expectedValue, currentValue, verificationSkippedReason }],\n" +
        "  restoredViewModelPropertyCount: number,\n" +
        "  restoredViewModelProperties: [{ propertyName, verified: boolean, expectedValue, currentValue, verificationSkippedReason }],\n" +
        "  skippedViewModelPropertyCount: number,\n" +
        "  skippedViewModelProperties: [{ propertyName, reason, verified: boolean, expectedValue, currentValue, verificationSkippedReason }],\n" +
        "  restoredFocus: boolean,\n" +
        "  warnings: string[]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"snapshotId\" -> snapshot missing, expired, or created for another process\n" +
        "- \"not connected\" -> reconnect before restore\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, snapshotId: \"snapshot_abc\" }")]
    public static Task<CallToolResult> RestoreStateSnapshot(
        SessionManager sessionManager,
        [Description("Snapshot ID returned by capture_state_snapshot.")] string snapshotId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Remove the stored snapshot after restore succeeds. Defaults to true.")] bool removeAfterRestore = true,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("snapshotId", snapshotId),
            ("removeAfterRestore", removeAfterRestore));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<RestoreStateSnapshotTool>(
                nameof(RestoreStateSnapshotTool),
                () => new RestoreStateSnapshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
