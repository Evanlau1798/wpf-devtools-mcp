namespace WpfDevTools.Mcp.Server.McpTools;

internal static class MutationBatchMcpToolDescriptions
{
    private const string BatchMetadata = "CATEGORY: State\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string BatchMutate =
        "Use this tool to execute multiple WPF runtime mutations in a single ordered batch.\n\n" +
        BatchMetadata +
        "USE WHEN: You need an ordered multi-step mutation workflow, shared snapshot/diff context, or one failure surface instead of many separate mutation calls.\n" +
        "DO NOT USE: When you need automatic rollback, when a single mutation call is sufficient, or when you have not captured an explicit snapshot for destructive experimentation.\n\n" +
        "SEQUENTIAL SEMANTICS: Mutations run in order, stop on the first failure, and do not roll back automatically.\n" +
        "FAILURE RECOVERY: When step N fails after steps 1..N-1 succeeded, the target application is left in a partially mutated state. " +
        "If captureSnapshot was provided and rollback.available=true, call restore_state_snapshot using rollback.params.snapshotId or recovery.params.snapshotId. " +
        "If rollback.available=false or recovery.tool is absent, the captured snapshot is not currently retained and manual reversal via inverse mutations (e.g. set_dp_value with the prior value) is required.\n" +
        "ROLLBACK GUIDANCE: Captured snapshots are retained per process for up to 20 snapshots or 30 minutes. " +
        "Failures include restore_state_snapshot guidance only while that snapshot remains retained; otherwise inspect rollback.available and recovery.tool before retrying.\n" +
        "DIFF SUPPORT: Set includeDiff=true together with captureSnapshot to compute get_state_diff after all mutations succeed.\n\n" +
        "PARAMETER SUMMARY:\n" +
        "- processId: optional process id; omit only when an active MCP session is already selected.\n" +
        "- elementId: optional default element id shared by nested mutations that do not provide their own elementId.\n" +
        "- captureSnapshot: optional object with elementId, propertyNames, viewModelPropertyNames, includeFocus, and snapshotName fields.\n" +
        "- includeDiff: optional boolean; set true only together with captureSnapshot when you need a post-batch get_state_diff.\n" +
        "- trigger: optional label forwarded to get_state_diff.\n" +
        "- mutations: required non-empty array of objects or a stringified JSON array. Each item has tool, optional label, and optional args object fields.\n" +
        "SCHEMA NOTES: Nested args must not include processId; use the batch root processId only. includeDiff=true requires captureSnapshot so get_state_diff has a baseline snapshot.\n\n" +
        "SUPPORTED MUTATION TOOLS: modify_viewmodel, set_dp_value, clear_dp_value, execute_command, click_element, fire_routed_event, focus_element, scroll_to_element, simulate_keyboard, override_style_setter, drag_and_drop.\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"mutations\": [{ \"tool\": \"modify_viewmodel\", \"args\": { \"propertyName\": \"Name\", \"value\": \"Alice\" } }, { \"tool\": \"modify_viewmodel\", \"args\": { \"propertyName\": \"Age\", \"value\": 30 } }] }\n" +
        "- { \"processId\": 12345, \"captureSnapshot\": { \"propertyNames\": [\"Text\"], \"viewModelPropertyNames\": [\"Name\"] }, \"includeDiff\": true, \"mutations\": [{ \"tool\": \"modify_viewmodel\", \"args\": { \"propertyName\": \"Name\", \"value\": \"Batch User\" } }] }\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - error (optional): string,\n" +
        "  - errorCode (optional): string,\n" +
        "  - recovery (optional): { suggestedAction, hint, tool (optional), params (optional) },\n" +
        "  - executionMode: 'sequential-stop-on-error',\n" +
        "  - mutationCount: number,\n" +
        "  - executedMutationCount: number,\n" +
        "  - successfulMutationCount: number,\n" +
        "  - failedMutationCount: number,\n" +
        "  - skippedMutationCount: number,\n" +
        "  - stateAfterTimeoutUnknown (optional): boolean,\n" +
        "  - requiresReconnect (optional): boolean,\n" +
        "  - snapshotId (optional): string,\n" +
        "  - stateDiff (optional): object,\n" +
        "  - rollback (optional): { available: boolean, snapshotId (optional), tool (optional), params (optional) },\n" +
        "  - mutations: [{ index: number, tool: string, label (optional): string, success: boolean, skipped: boolean, error (optional): string, errorCode (optional): string, stateAfterTimeoutUnknown (optional): boolean, result (optional): object }]\n\n" +
        "ERRORS:\n" +
        "- InvalidArgument: mutations array is empty, missing, malformed, or contains unsupported nested processId overrides.\n" +
        "- BatchStepFailed: a mutation step returned success=false; failed/skipped counts identify where execution stopped.\n" +
        "- OperationFailed: captureSnapshot was requested but the snapshot call failed before mutation execution.\n" +
        "- DiffFailed: includeDiff was true but get_state_diff failed after mutations succeeded.\n" +
        "- Timeout: a mutation or get_state_diff was canceled/timed out after snapshot capture; stateAfterTimeoutUnknown=true means reconnect, then inspect rollback.available and recovery.tool before restoring or retrying.";
}
