using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Event Drain tools.
/// </summary>
[McpServerToolType]
public static class EventDrainMcpTools
{

    [McpServerTool(Name = "drain_events", Title = "Drain Pending Runtime Events", OpenWorld = false, ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(EventDrainMcpToolDescriptions.DrainEvents)]
    public static Task<CallToolResult> DrainEvents(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional maximum number of buffered events to return. Omit to use the normal live drain window only when no replay is buffered; if replay is already buffered, omitting maxEvents returns the full merged replay plus matching live backlog. Provide a positive integer to enforce a caller-visible result cap after the server internally drains matching live events uncapped.")] int? maxEvents = null,
        [AllowedValues("all", "DpChange", "RoutedEvent", "BindingError", "ValidationChange")]
        [Description("Optional event-type filter, such as DpChange or RoutedEvent. Omit or pass [\"all\"] to read all buffered event types.")] string[]? eventTypes = null,
        [Description("Optional element ID filter. Only matching buffered events will be returned.")] string? elementId = null,
        [Description("Optional ISO-8601 lower-bound timestamp. Only buffered events at or after this time will be returned.")] string? sinceTimestamp = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("maxEvents", maxEvents),
            ("eventTypes", eventTypes),
            ("elementId", elementId),
            ("sinceTimestamp", sinceTimestamp));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<DrainEventsTool>(sessionManager, "DrainEventsTool", () => new DrainEventsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "drain_events");
    }
}
