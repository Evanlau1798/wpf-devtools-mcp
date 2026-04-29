using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Mutation Batch tools.
/// </summary>
[McpServerToolType]
public static class MutationBatchMcpTools
{
    private const string BatchMetadata = "CATEGORY: State\n\n";

    [McpServerTool(Name = "batch_mutate", Title = "Execute Sequential WPF Runtime Mutations", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to execute multiple WPF runtime mutations in a single ordered batch.\n\n" +
        BatchMetadata +
        "USE WHEN: You need an ordered multi-step mutation workflow, shared snapshot/diff context, or one failure surface instead of many separate mutation calls.\n" +
        "DO NOT USE: When you need automatic rollback, when a single mutation call is sufficient, or when you have not captured an explicit snapshot for destructive experimentation.\n\n" +
        "SEQUENTIAL SEMANTICS: Mutations run in order, stop on the first failure, and do not roll back automatically.\n" +
        "FAILURE RECOVERY: When step N fails after steps 1..N-1 succeeded, the target application is left in a partially mutated state. " +
        "If captureSnapshot was provided, call restore_state_snapshot(snapshotId) using the returned snapshotId to revert. " +
        "If no snapshot was captured, manual reversal via inverse mutations (e.g. set_dp_value with the prior value) is required.\n" +
        "ROLLBACK GUIDANCE: When captureSnapshot is provided, failures include explicit restore_state_snapshot guidance instead of hidden transaction behavior.\n" +
        "DIFF SUPPORT: Set includeDiff=true together with captureSnapshot to compute get_state_diff after all mutations succeed.\n\n" +
        "REQUEST FORMAT:\n" +
        "{\n" +
        "  processId?: number,\n" +
        "  elementId?: string,\n" +
        "  captureSnapshot?: { elementId?, propertyNames?, viewModelPropertyNames?, includeFocus?, snapshotName? },\n" +
        "  includeDiff?: boolean,\n" +
        "  trigger?: string,\n" +
        "  mutations: [{ tool, label?, args?: object }]\n" +
        "}\n\n" +
        "SUPPORTED MUTATION TOOLS: modify_viewmodel, set_dp_value, clear_dp_value, execute_command, click_element, fire_routed_event, focus_element, scroll_to_element, simulate_keyboard, override_style_setter, drag_and_drop.\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, mutations: [{ tool: \"modify_viewmodel\", args: { propertyName: \"Name\", value: \"Alice\" } }, { tool: \"modify_viewmodel\", args: { propertyName: \"Age\", value: 30 } }] }\n" +
        "- { processId: 12345, captureSnapshot: { propertyNames: [\"Text\"], viewModelPropertyNames: [\"Name\"] }, includeDiff: true, mutations: [{ tool: \"modify_viewmodel\", args: { propertyName: \"Name\", value: \"Batch User\" } }] }\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  error?: string,\n" +
        "  errorCode?: string,\n" +
        "  recovery?: { suggestedAction, hint, tool?, params? },\n" +
        "  executionMode: 'sequential-stop-on-error',\n" +
        "  mutationCount: number,\n" +
        "  executedMutationCount: number,\n" +
        "  successfulMutationCount: number,\n" +
        "  failedMutationCount: number,\n" +
        "  skippedMutationCount: number,\n" +
        "  stateAfterTimeoutUnknown?: boolean,\n" +
        "  requiresReconnect?: boolean,\n" +
        "  snapshotId?: string,\n" +
        "  stateDiff?: object,\n" +
        "  rollback?: { available: boolean, snapshotId?, tool?, params? },\n" +
        "  mutations: [{ index: number, tool: string, label?: string, success: boolean, skipped: boolean, error?: string, errorCode?: string, stateAfterTimeoutUnknown?: boolean, result?: object }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- InvalidArgument: mutations array is empty, missing, malformed, or contains unsupported nested processId overrides.\n" +
        "- BatchStepFailed: a mutation step returned success=false; failed/skipped counts identify where execution stopped.\n" +
        "- OperationFailed: captureSnapshot was requested but the snapshot call failed before mutation execution.\n" +
        "- DiffFailed: includeDiff was true but get_state_diff failed after mutations succeeded.\n" +
        "- Timeout: a mutation or get_state_diff was canceled/timed out after snapshot capture; stateAfterTimeoutUnknown=true means reconnect and restore before retrying.")]
    public static Task<CallToolResult> BatchMutate(
        SessionManager sessionManager,
        [Description("Mutation steps as a JSON array. Each step must include tool (string) and may include label (string) plus args (object). Example: [{ \"tool\": \"set_dp_value\", \"args\": { \"propertyName\": \"Width\", \"value\": 100 } }]")] JsonElement? mutations = null,
        [Description("Optional capture_state_snapshot request as a JSON object. Required when includeDiff=true. Example: { \"propertyNames\": [\"Text\"], \"viewModelPropertyNames\": [\"Name\"] }")] JsonElement? captureSnapshot = null,
        [Description("Optional flag to run get_state_diff after all mutations succeed. Requires captureSnapshot.")] bool includeDiff = false,
        [Description("Optional trigger label forwarded to get_state_diff. Defaults to 'batch_mutate'.")] string? trigger = null,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional default element ID injected into mutation steps that do not specify elementId explicitly.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("captureSnapshot", captureSnapshot),
            ("includeDiff", includeDiff),
            ("trigger", trigger),
            ("mutations", mutations));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<BatchMutateTool>(sessionManager, 
                nameof(BatchMutateTool),
                () => new BatchMutateTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "batch_mutate");
    }
}
