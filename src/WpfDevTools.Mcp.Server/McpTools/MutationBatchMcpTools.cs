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

    [McpServerTool(Name = "batch_mutate", Title = "Execute Sequential WPF Runtime Mutations", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(MutationBatchMcpToolDescriptions.BatchMutate)]
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
