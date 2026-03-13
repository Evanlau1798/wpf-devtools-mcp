using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for RoutedEvent tools (3 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class EventMcpTools
{
    private const string EventMetadata = "CATEGORY: Event | SAFETY: Check the SDK ReadOnly and Destructive flags before invoking this tool.\n\n";
    private const string RuntimeNavigationGuidance = "FOLLOW-UP GUIDANCE: Successful responses may include runtime-computed `nextSteps`; prefer those returned follow-ups over ad hoc tool guessing.\n\n";

    [McpServerTool(Name = "trace_routed_events", Title = "Trace WPF Routed Events", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
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
        "  { success, mode: \"capture\", eventName, duration, isTracing, eventCount, events, handlerInvocationCount }\n" +
        "- start mode:\n" +
        "  { success, mode: \"start\", eventName, requestedDuration, effectiveDuration, isTracing, message }\n" +
        "  NOTE: effectiveDuration may be higher than requestedDuration (minimum 30s enforced for AI agent IPC round-trips)\n" +
        "- get mode:\n" +
        "  { success, mode: \"get\", isTracing, eventCount, events, handlerInvocationCount }\n\n" +
        "TIP: For AI-driven automation, prefer `mode=\"start\"` + `click_element`/`fire_routed_event` + `mode=\"get\"` so the capture window is not blocked by the current request.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"invalid event name\" -> verify eventName is a valid WPF RoutedEvent\n" +
        "- \"eventName required\" -> required for `capture` and `start` modes\n" +
        "- \"invalid mode\" -> use `capture`, `start`, or `get`\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, eventName: \"MouseDown\", durationMs: 500 }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", eventName: \"Click\", mode: \"start\", durationMs: 1000 }\n" +
        "- { processId: 12345, mode: \"get\" }")]
    public static Task<CallToolResult> TraceRoutedEvents(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional WPF routed event name to trace, such as Click or MouseDown. Required for `capture` and `start`, optional for `get`.")] string? eventName = null,
        [Description("Optional element ID to scope the event trace. Omit for the root window.")] string? elementId = null,
        [Description("Optional capture window in milliseconds (default: 5000). Use smaller values (250-2000) for interactive STDIO sessions.")] int? durationMs = null,
        [Description("Optional tracing mode: `capture` (default), `start`, or `get`. Use `start` + `get` for AI-friendly non-blocking workflows.")] string? mode = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName),
            ("duration", durationMs),
            ("mode", mode));

        var timeoutSeconds = Math.Max(
            McpServerConfiguration.DefaultToolTimeoutSeconds,
            (int)Math.Ceiling(((durationMs ?? 5000) / 1000d) + 2));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<TraceRoutedEventsTool>("TraceRoutedEventsTool", () => new TraceRoutedEventsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            timeoutSeconds: timeoutSeconds);
    }

    [McpServerTool(Name = "get_event_handlers", Title = "Inspect WPF Event Handlers", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
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
        "- { processId: 12345, elementId: \"SaveButton\", eventName: \"Click\" }")]
    public static Task<CallToolResult> GetEventHandlers(
        SessionManager sessionManager,
        [Description("WPF routed event name whose handlers should be listed.")] string eventName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose handlers should be inspected. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetEventHandlersTool>("GetEventHandlersTool", () => new GetEventHandlersTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "fire_routed_event", Title = "Fire WPF Routed Event", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
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
        "DETAIL MODE: Optional `detail` controls additive metadata. Use `standard` (default) for requested/effective input + observedEffect, or `compact` to keep only the core routed-event result while still preserving semantically relevant fallback indicators.\n\n" +
        RuntimeNavigationGuidance +
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
        "- { processId: 12345, elementId: \"SaveButton\", eventName: \"Click\" }\n" +
        "- { processId: 12345, elementId: \"Panel1\", eventName: \"MouseDown\" }")]
    public static Task<CallToolResult> FireRoutedEvent(
        SessionManager sessionManager,
        [Description("WPF routed event name to raise, such as Click.")] string eventName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional target element ID that should receive the routed event.")] string? elementId = null,
        [Description("Optional JSON payload for custom routed event arguments. Currently unused for standard RoutedEvents (Click, MouseDown); reserved for custom events.")] JsonElement? eventArgs = null,
        [Description("Optional metadata detail mode: 'standard' (default) or 'compact'.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName),
            ("eventArgs", eventArgs),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<FireRoutedEventTool>("FireRoutedEventTool", () => new FireRoutedEventTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "fire_routed_event");
    }
}
