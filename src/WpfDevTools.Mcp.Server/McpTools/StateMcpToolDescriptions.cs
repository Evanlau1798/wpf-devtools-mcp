namespace WpfDevTools.Mcp.Server.McpTools;

internal static class StateMcpToolDescriptions
{
    private const string StateMetadata = "CATEGORY: State\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string CaptureStateSnapshot =
        "Capture connected WPF state before mutations. Snapshots are session-only (30 minutes; 20 per process).\n\n" +
        StateMetadata +
        "FLOW: capture_state_snapshot -> snapshotId -> get_state_diff -> restore_state_snapshot. Pass snapshotId to diff and restore.\n" +
        "Binding-backed/two-way DP: capture propertyNames and viewModelPropertyNames; use includeFocus for focus or command state.\n" +
        "VIEWMODEL LIMIT: Only scalar values restore deterministically. skippedViewModelProperties reports complex values; capture a scalar key or selection DP instead.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success, snapshotId,\n" +
        "  - snapshotSummary: { dependencyPropertyCount, skippedDependencyPropertyCount, viewModelPropertyCount, restorableViewModelPropertyCount, skippedViewModelPropertyCount, capturedFocus },\n" +
        "  - skippedDependencyProperties: [{ propertyName, reason, errorCode }],\n" +
        "  - skippedViewModelProperties: [{ propertyName, propertyType, reason }]\n\n" +
        "ERROR: Choose propertyNames, viewModelPropertyNames, or includeFocus.\n\n";

    public const string RestoreStateSnapshot =
        "Restore a captured WPF runtime state snapshot.\n\n" +
        StateMetadata +
        "USE WHEN: Rolling back temporary DependencyProperty, ViewModel, or focus changes in the same session.\n" +
        "DO NOT USE: Across disconnected sessions, application restarts, or after the in-memory snapshot has expired.\n" +
        "RETENTION: 30 minutes; 20 snapshots per process.\n\n" +
        "MINIMAL ROLLBACK CHAIN: capture_state_snapshot -> snapshotId -> get_state_diff -> restore_state_snapshot. restore_state_snapshot requires the explicit snapshotId returned by capture_state_snapshot or batch_mutate.\n\n" +
        "EXPRESSION ROLLBACK: Binding expressions can be restored. For two-way bindings, also capture the source ViewModel property. Other expressions appear in skippedDependencyProperties.\n" +
        "READ-ONLY DP: ScrollViewer offsets are skipped; recover with scroll_to_element or app navigation, then verify.\n" +
        "VIEWMODEL LIMITS: Complex reference ViewModel properties may be skipped when object identity cannot be reconstructed from the captured value; skipped entries include restoreDisposition, reason, verification fields, and follow-up guidance for re-reading or recapturing the property.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - restoredDependencyPropertyCount: number,\n" +
        "  - restoredDependencyProperties: [{ propertyName, verified: boolean, expectedValue, currentValue, expectedIsExpression, currentIsExpression, verificationSkippedReason }],\n" +
        "  - skippedDependencyPropertyCount: number,\n" +
        "  - skippedDependencyProperties: [{ propertyName, reason, restoreDisposition, verified: boolean, expectedValue, currentValue, verificationSkippedReason }],\n" +
        "  - restoredViewModelPropertyCount: number,\n" +
        "  - restoredViewModelProperties: [{ propertyName, verified: boolean, expectedValue, currentValue, verificationSkippedReason }],\n" +
        "  - skippedViewModelPropertyCount: number,\n" +
        "  - skippedViewModelProperties: [{ propertyName, reason, restoreDisposition, verified: boolean, expectedValue, currentValue, verificationSkippedReason }],\n" +
        "  - restoredFocus: boolean,\n" +
        "  - warnings: string[],\n" +
        "  - follow-up guidance for failed DependencyProperty verification or skipped complex ViewModel properties\n\n" +
        "ERRORS:\n" +
        "- \"snapshotId\" -> snapshot missing, expired, already removed, or created for another process; take a fresh snapshot with capture_state_snapshot. If restore conflicts persist, inspect get_dp_value_source and get_bindings before retrying\n" +
        "- \"not connected\" -> reconnect before restore\n\n";
}
