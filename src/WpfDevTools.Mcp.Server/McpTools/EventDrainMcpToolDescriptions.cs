namespace WpfDevTools.Mcp.Server.McpTools;

internal static class EventDrainMcpToolDescriptions
{
    private const string EventMetadata = "CATEGORY: Event\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string DrainEvents =
        "Use this tool to explicitly drain pending runtime watch events that were buffered by prior DependencyProperty or routed-event activity.\n\n" +
        EventMetadata + "Return and clear buffered runtime events from the current Inspector session. " +
        "Use it when you want deterministic event consumption instead of waiting for piggyback on a later tool response.\n\n" +
        "USE WHEN: You have active DP watches or routed-event traces and need an explicit read step; you want to filter buffered events by type, element, or time window.\n" +
        "DO NOT USE: As a long-running subscription. This drains a bounded in-memory buffer.\n\n" +
        "PRIOR CONTEXT: Piggybacked pendingEvents on other tool responses can set pendingEventsMayIncludePriorContext=true, meaning those events may include prior context from before the current tool call. For a clean action window, call drain_events before the action with the narrowest useful filters, perform the action or mutation, then call drain_events again to read only that action window.\n\n" +
        "REPLAY SEMANTICS: When replay is already buffered, the server performs an uncapped live read internally, then applies maxEvents across the merged replay + live event set. Any replay event that is not returned by the explicit read, and any matching live event that exceeds the caller-visible result cap, remain buffered for the next explicit drain_events call. If that live drain fails before merge completes, the error surfaces errorData.replayPreserved plus errorData.bufferedReplayEventCount so callers can retry without assuming the preserved replay buffer was discarded.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - pendingEventCount: number,\n" +
        "  - droppedEventCount: number,\n" +
        "  - cleanupIncomplete (optional): boolean,\n" +
        "  - cleanupFailureMessage (optional): string,\n" +
        "  - cleanupFailureType (optional): string,\n" +
        "  - pendingEvents (optional): [{ eventType, elementId, propertyName (optional), eventName (optional), timestampUtc }]\n\n" +
        "If post-drain cleanup cannot finish cleanly, the response surfaces cleanupIncomplete plus optional cleanupFailureMessage and cleanupFailureType so callers can quarantine or retry follow-up workflows explicitly.\n\n" +
        "ERRORS:\n" +
        "- \"maxEvents\" invalid -> provide a positive integer when filtering the drain size\n" +
        "- \"sinceTimestamp\" invalid -> provide an ISO-8601 timestamp\n\n";
}
