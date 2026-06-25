using System.Text.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for DependencyProperty tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class DependencyPropertyMcpTools
{

    [McpServerTool(Name = "get_dp_value_source", Title = "Inspect DependencyProperty Value Source", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(DependencyPropertyMcpToolDescriptions.GetDpValueSource)]
    public static Task<CallToolResult> GetDpValueSource(
        SessionManager sessionManager,
        [Description("Optional DependencyProperty name to inspect, such as Text or IsEnabled. Omit only when propertyNames is provided for batch inspection.")] string? propertyName = null,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [Description("Optional list of element IDs for batch inspection. Use either elementId or elementIds, not both.")] string[]? elementIds = null,
        [Description("Optional list of property names for batch inspection. Use either propertyName or propertyNames, not both.")] string[]? propertyNames = null,
        [Description("Optional compact response mode. When true, only return the minimum decision-making fields for each result.")] bool compact = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds),
            ("propertyName", propertyName),
            ("propertyNames", propertyNames),
            ("compact", compact));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetDpValueSourceTool>(sessionManager, "GetDpValueSourceTool", () => new GetDpValueSourceTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_dp_metadata", Title = "Inspect DependencyProperty Metadata", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(DependencyPropertyMcpToolDescriptions.GetDpMetadata)]
    public static Task<CallToolResult> GetDpMetadata(
        SessionManager sessionManager,
        [Description("DependencyProperty name whose metadata should be returned.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID used to resolve owner-specific metadata.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetDpMetadataTool>(sessionManager, "GetDpMetadataTool", () => new GetDpMetadataTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "set_dp_value", Title = "Set WPF DependencyProperty Value", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(DependencyPropertyMcpToolDescriptions.SetDpValue)]
    public static Task<CallToolResult> SetDpValue(
        SessionManager sessionManager,
        [Description("DependencyProperty name to set at runtime.")] string propertyName,
        [Description("New property value encoded as raw JSON.")] JsonElement value,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [AllowedValues("compact", "minimal", "verbose", "standard")]
        [Description("Optional metadata detail mode: omit or use 'compact' (default), use 'minimal' for success/property/newValue confirmation only, use 'verbose' for full additive metadata, or 'standard' as a compatibility alias.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("value", value),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<SetDpValueTool>(sessionManager, "SetDpValueTool", () => new SetDpValueTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "set_dp_value");
    }

    [McpServerTool(Name = "clear_dp_value", Title = "Clear WPF DependencyProperty Value", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(DependencyPropertyMcpToolDescriptions.ClearDpValue)]
    public static Task<CallToolResult> ClearDpValue(
        SessionManager sessionManager,
        [Description("DependencyProperty name whose local value should be cleared.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [AllowedValues("compact", "minimal", "verbose", "standard")]
        [Description("Optional metadata detail mode: omit or use 'compact' (default), use 'minimal' for success/property/newValue confirmation only, use 'verbose' for full additive metadata, or 'standard' as a compatibility alias.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ClearDpValueTool>(sessionManager, "ClearDpValueTool", () => new ClearDpValueTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "clear_dp_value");
    }

    [McpServerTool(Name = "watch_dp_changes", Title = "Watch WPF DependencyProperty Changes", OpenWorld = false, ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(DependencyPropertyMcpToolDescriptions.WatchDpChanges)]
    public static Task<CallToolResult> WatchDpChanges(
        SessionManager sessionManager,
        [Description("DependencyProperty name to watch for runtime changes.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<WatchDpChangesTool>(sessionManager, "WatchDpChangesTool", () => new WatchDpChangesTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "wait_for_dp_change", Title = "Wait For WPF DependencyProperty Change", OpenWorld = false, ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description(DependencyPropertyMcpToolDescriptions.WaitForDpChange)]
    public static Task<CallToolResult> WaitForDpChange(
        SessionManager sessionManager,
        [Description("DependencyProperty name to monitor for changes.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [Description("Optional timeout in milliseconds. Default: 5000. Maximum: 25000.")] int? timeoutMs = null,
        [Description("Optional polling interval in milliseconds. Default: 200.")] int? pollIntervalMs = null,
        [Description("Optional expected property value. Omit to stop on any value change.")] JsonElement? expectedValue = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("timeoutMs", timeoutMs),
            ("pollIntervalMs", pollIntervalMs),
            ("expectedValue", expectedValue));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<WaitForDpChangeTool>(sessionManager, "WaitForDpChangeTool", () => new WaitForDpChangeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "wait_for_dp_change_after_mutation", Title = "Wait For WPF DependencyProperty Change After Mutation", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(DependencyPropertyMcpToolDescriptions.WaitForDpChangeAfterMutation)]
    public static Task<CallToolResult> WaitForDpChangeAfterMutation(
        SessionManager sessionManager,
        [Description("DependencyProperty name to monitor for changes after the mutation runs.")] string propertyName,
        [Description("Single mutation step as a JSON object, using the same shape as one batch_mutate item. Use args, not arguments: { \"tool\": \"set_dp_value\", \"args\": { \"propertyName\": \"Width\", \"value\": 100 } }.")] JsonElement triggerMutation,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [Description("Optional timeout in milliseconds. Default: 5000. Maximum: 25000.")] int? timeoutMs = null,
        [Description("Optional polling interval in milliseconds. Default: 200.")] int? pollIntervalMs = null,
        [Description("Optional expected property value. Omit to stop on any value change.")] JsonElement? expectedValue = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("timeoutMs", timeoutMs),
            ("pollIntervalMs", pollIntervalMs),
            ("expectedValue", expectedValue),
            ("triggerMutation", triggerMutation));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<WaitForDpChangeTool>(sessionManager, "WaitForDpChangeTool", () => new WaitForDpChangeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "wait_for_dp_change_after_mutation");
    }
}
