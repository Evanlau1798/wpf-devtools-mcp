namespace WpfDevTools.Mcp.Server.McpTools;

internal static class StateMcpToolDescriptions
{
    private const string StateMetadata = "CATEGORY: State\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string CaptureStateSnapshot =
        "Use this tool to capture a WPF runtime state snapshot before mutations or multi-step debugging.\n\n" +
        StateMetadata + "Capture a restorable runtime snapshot for a connected WPF process.\n\n" +
        "USE WHEN: Before mutation-heavy debugging, demos, or regression flows where rollback matters.\n" +
        "DO NOT USE: As durable persistence; snapshots are in-memory and session-scoped only.\n" +
        "RETENTION: The server retains at most 20 snapshots per process for up to 30 minutes; capture a fresh snapshot before long mutation sequences.\n\n" +
        "MINIMAL ROLLBACK CHAIN: capture_state_snapshot -> snapshotId -> get_state_diff -> restore_state_snapshot. Always pass the returned snapshotId explicitly to diff and restore.\n\n" +
        "BOUND DP ROLLBACK: For Binding-backed or two-way DependencyProperty changes, capture both the target DP in propertyNames and the source property in viewModelPropertyNames. Set includeFocus=true when command validation, keyboard routing, or focused element state is part of the workflow.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - snapshotId: string,\n" +
        "  - snapshotSummary: { dependencyPropertyCount, skippedDependencyPropertyCount, viewModelPropertyCount, capturedFocus },\n" +
        "  - skippedDependencyProperties: [{ propertyName, reason, errorCode }] when individual DependencyProperty captures are skipped but another requested state dimension is captured\n\n" +
        "ERRORS:\n" +
        "- \"propertyNames / viewModelPropertyNames / includeFocus required\" -> choose at least one capture dimension\n\n";

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
