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

    [McpServerTool(Name = "capture_state_snapshot", Title = "Capture WPF Runtime State Snapshot", OpenWorld = false, ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(StateMcpToolDescriptions.CaptureStateSnapshot)]
    public static Task<CallToolResult> CaptureStateSnapshot(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional element ID whose state should be captured. Omit for the root window.")] string? elementId = null,
        [Description("Optional DependencyProperty names to capture as restorable local-value state. At most 100 names, each 256 characters or fewer; duplicates are ignored. Binding-backed expressions are captured with same-session restore handles when possible; non-Binding expressions remain skipped capability boundaries.")] string[]? propertyNames = null,
        [Description("Optional ViewModel property names to capture from the current DataContext. At most 100 names, each 256 characters or fewer; duplicates are ignored.")] string[]? viewModelPropertyNames = null,
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
            (a, ct) => ToolCallHelper.CachedTool<CaptureStateSnapshotTool>(sessionManager, 
                nameof(CaptureStateSnapshotTool),
                () => new CaptureStateSnapshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "capture_state_snapshot");
    }

    [McpServerTool(Name = "restore_state_snapshot", Title = "Restore WPF Runtime State Snapshot", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(StateMcpToolDescriptions.RestoreStateSnapshot)]
    public static Task<CallToolResult> RestoreStateSnapshot(
        SessionManager sessionManager,
        [Description("Snapshot ID returned by capture_state_snapshot.")] string snapshotId,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Remove the stored snapshot after restore succeeds. Defaults to true.")] bool removeAfterRestore = true,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("snapshotId", snapshotId),
            ("removeAfterRestore", removeAfterRestore));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<RestoreStateSnapshotTool>(sessionManager, 
                nameof(RestoreStateSnapshotTool),
                () => new RestoreStateSnapshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
