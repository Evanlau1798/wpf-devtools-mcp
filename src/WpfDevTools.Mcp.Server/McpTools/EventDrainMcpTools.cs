using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Event Drain tools (2 tools).
/// </summary>
[McpServerToolType]
public static class EventDrainMcpTools
{
    private const string EventMetadata = "CATEGORY: Event\n\n";

    [McpServerTool(Name = "drain_events", Title = "Drain Pending Runtime Events", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to explicitly drain pending runtime watch events that were buffered by prior DependencyProperty or routed-event activity.\n\n" +
        EventMetadata + "[Event] Return and clear buffered runtime events from the current Inspector session. " +
        "Use it when you want deterministic event consumption instead of waiting for piggyback on a later tool response.\n\n" +
        "USE WHEN: You have active DP watches or routed-event traces and need an explicit read step; you want to filter buffered events by type, element, or time window.\n" +
        "DO NOT USE: As a long-running subscription. This drains a bounded in-memory buffer.\n\n" +
        "REPLAY SEMANTICS: When replay is already buffered, the server performs an uncapped live read internally, then applies maxEvents across the merged replay + live event set. Any replay event that is not returned by the explicit read, and any matching live event that exceeds the caller-visible result cap, remain buffered for the next explicit drain_events call. If that live drain fails before merge completes, the error surfaces errorData.replayPreserved plus errorData.bufferedReplayEventCount so callers can retry without assuming the preserved replay buffer was discarded.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  pendingEventCount: number,\n" +
        "  droppedEventCount: number,\n" +
        "  cleanupIncomplete?: boolean,\n" +
        "  cleanupFailureMessage?: string,\n" +
        "  cleanupFailureType?: string,\n" +
        "  pendingEvents?: [{ eventType, elementId, propertyName?, eventName?, timestampUtc }]\n" +
        "}\n\n" +
        "If post-drain cleanup cannot finish cleanly, the response surfaces cleanupIncomplete plus optional cleanupFailureMessage and cleanupFailureType so callers can quarantine or retry follow-up workflows explicitly.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"maxEvents\" invalid -> provide a positive integer when filtering the drain size\n" +
        "- \"sinceTimestamp\" invalid -> provide an ISO-8601 timestamp\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, maxEvents: 10, eventTypes: [\"DpChange\"] }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", sinceTimestamp: \"2026-03-14T10:00:00.0000000Z\" }")]
    public static Task<CallToolResult> DrainEvents(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional maximum number of buffered events to return. Omit to use the normal live drain window only when no replay is buffered; if replay is already buffered, omitting maxEvents returns the full merged replay plus matching live backlog. Provide a positive integer to enforce a caller-visible result cap after the server internally drains matching live events uncapped.")] int? maxEvents = null,
        [Description("Optional event-type filter, such as DpChange or RoutedEvent.")] string[]? eventTypes = null,
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
            (a, ct) => ToolCallHelper.CachedTool<DrainEventsTool>("DrainEventsTool", () => new DrainEventsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "drain_events");
    }
}
