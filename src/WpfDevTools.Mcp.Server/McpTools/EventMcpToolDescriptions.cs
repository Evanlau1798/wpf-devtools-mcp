namespace WpfDevTools.Mcp.Server.McpTools;

internal static class EventMcpToolDescriptions
{
    private const string EventMetadata = "CATEGORY: Event\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string TraceRoutedEvents =
        "Use this tool to trace WPF routed events during runtime interaction and diagnose event flow.\n\n" +
        EventMetadata + "[Event] Trace a routed event over a short capture window and return collected records, or run in a two-step non-blocking workflow for AI agents. " +
        "Use `mode=\"start\"` to start tracing immediately, then trigger UI activity with another tool, then call `mode=\"get\"` to fetch the buffered records.\n\n" +
        "USE WHEN: Debugging event handling issues; confirming whether Click/MouseDown style events are firing; correlating routed events with follow-up tool calls from the same MCP session.\n" +
        "DO NOT USE: As a long-running subscription. This tool captures only a bounded in-memory trace.\n\n" +
        "MODES:\n" +
        "- `capture` (default): start tracing, wait for the capture window to end, then return the collected records\n" +
        "- `start`: start tracing immediately and return without blocking the session\n" +
        "- `get`: return the current buffered trace and tracing status; `eventName` is not required in this mode\n\n" +
        "RESPONSE FORMAT:\n" +
        "- capture mode:\n" +
        "  { success, mode: \"capture\", eventName, duration, isTracing, eventCount, events, handlerInvocationCount, cleanupState?, cleanupFailed?, cleanupIncomplete? }\n" +
        "- start mode:\n" +
        "  { success, mode: \"start\", eventName, requestedDuration, effectiveDuration, shortDurationOverrideUsed, isTracing, message }\n" +
        "  NOTE: effectiveDuration may be higher than requestedDuration (minimum 30s enforced by default for AI agent IPC round-trips). Set `allowShortStartDuration=true` to opt into a shorter explicit window.\n" +
        "- get mode:\n" +
        "  { success, mode: \"get\", isTracing, eventCount, totalEventCount, returnedEventCount, eventsTruncated, maxEvents, events, handlerInvocationCount, cleanupState?, cleanupFailed?, cleanupIncomplete? }\n" +
        "  NOTE: Provide maxEvents to cap returned trace records. When capped, totalEventCount preserves the original count and eventsTruncated=true signals that more events were available.\n\n" +
        "TIP: For AI-driven automation, prefer `mode=\"start\"` + `click_element`/`fire_routed_event` + `mode=\"get\"` so the capture window is not blocked by the current request.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"invalid event name\" -> verify eventName is a valid WPF RoutedEvent\n" +
        "- \"eventName required\" -> required for `capture` and `start` modes\n" +
        "- \"maxEvents\" invalid -> provide a positive integer when limiting returned trace records\n" +
        "- \"invalid mode\" -> use `capture`, `start`, or `get`\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"eventName\": \"MouseDown\", \"durationMs\": 500 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"eventName\": \"Click\", \"mode\": \"start\", \"durationMs\": 1000 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"eventName\": \"Click\", \"mode\": \"start\", \"durationMs\": 1000, \"allowShortStartDuration\": true }\n" +
        "- { \"processId\": 12345, \"mode\": \"get\", \"maxEvents\": 25 }";

    public const string GetEventHandlers =
        "Use this tool to inspect WPF event handlers attached to a runtime element.\n\n" +
        EventMetadata + "[Event] Get all event handlers attached to a WPF element for a specific routed event. " +
        "Returns handler method names, declaring types, and whether they handle tunneling/bubbling.\n\n" +
        "USE WHEN: Button click does nothing; need to verify event handlers are attached.\n" +
        "DO NOT USE: Without eventName - it's required.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  eventName,\n" +
        "  handlerCount: number,\n" +
        "  reflectionSupported: boolean,\n" +
        "  mayBeIncomplete: boolean,\n" +
        "  handlers: [{\n" +
        "    methodName, declaringType, handledEventsToo: boolean\n" +
        "  }]\n" +
        "}\n\n" +
        "Empty handlers array means no handlers were visible via reflection. Class handlers, commands, template triggers, and inaccessible internals may not appear.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid event name\" -> verify eventName is valid\n" +
        "- \"eventName required\" -> must specify which event\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"eventName\": \"Click\" }";

    public const string FireRoutedEvent =
        "Use this tool to fire a WPF routed event when you need runtime event semantics without a physical click.\n\n" +
        EventMetadata + "[Event] Raise a routed event on a WPF element. " +
        "Behavior depends on element type and event:\n" +
        "- ButtonBase + Click: calls OnClick() via reflection, which triggers BOTH RoutedEvent handlers AND ICommand execution (same as a real user click)\n" +
        "- All other combinations: calls UIElement.RaiseEvent(), which only triggers routed event handlers (Tunneling -> Direct -> Bubbling)\n\n" +
        "USE WHEN: Testing event handlers programmatically; triggering Click on buttons including ICommand execution; verifying routed event wiring.\n" +
        "DO NOT USE: For keyboard input (use simulate_keyboard); for complex click simulation with mouse coordinates (use click_element).\n\n" +
        "STANDARD EVENT ARGS: For common mouse events (for example MouseDown/MouseUp), the tool creates compatible WPF event args automatically; custom eventArgs remain optional.\n\n" +
        "SEMANTIC DIFFERENCE FROM click_element:\n" +
        "- fire_routed_event('Click') on ButtonBase: calls OnClick() (includes ICommand) - functionally equivalent to click_element for command execution\n" +
        "- fire_routed_event('Click') on non-ButtonBase: only fires routed event handlers, no ICommand\n" +
        "- click_element: calls OnClick() for ButtonBase descendants, selects TabItem; returns error for other element types\n\n" +
        "WARNING: This triggers real application logic. For ButtonBase+Click, ICommand WILL execute.\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep only the core routed-event result while still preserving semantically relevant fallback indicators. Use `minimal` for success/property/newValue confirmation only, `verbose` for requested/effective input + observedEffect, or legacy `standard` as a compatibility alias.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  message: string,\n" +
        "  eventName: string,\n" +
        "  usedOnClick: boolean  // ONLY present when ButtonBase+Click path was used; absent for other events\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid event name\" -> verify eventName is valid\n" +
        "- \"eventName required\" -> must specify which event\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"eventName\": \"Click\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"Panel1\", \"eventName\": \"MouseDown\" }";
}
