using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

[McpServerToolType]
public static class MutationBatchMcpTools
{
    private const string BatchMetadata = "CATEGORY: State | SAFETY: Check the SDK ReadOnly and Destructive flags before invoking this tool.\n\n";

    [McpServerTool(Name = "batch_mutate", Title = "Execute Sequential WPF Runtime Mutations", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to execute multiple WPF runtime mutations in a single ordered batch.\n\n" +
        BatchMetadata +
        "SEQUENTIAL SEMANTICS: Mutations run in order, stop on the first failure, and do not roll back automatically.\n" +
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
        "- { processId: 12345, captureSnapshot: { propertyNames: [\"Text\"], viewModelPropertyNames: [\"Name\"] }, includeDiff: true, mutations: [{ tool: \"modify_viewmodel\", args: { propertyName: \"Name\", value: \"Batch User\" } }] }")]
    public static Task<CallToolResult> BatchMutate(
        SessionManager sessionManager,
        [Description("Mutation steps encoded as a JSON array or a stringified JSON array for compatibility. Each step must include tool and may include label plus args.")] object? mutations = null,
        [Description("Optional capture_state_snapshot request encoded as a raw JSON object or a stringified JSON object for compatibility. Required when includeDiff=true.")] object? captureSnapshot = null,
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
            (a, ct) => ToolCallHelper.CachedTool<BatchMutateTool>(
                nameof(BatchMutateTool),
                () => new BatchMutateTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "batch_mutate");
    }
}
