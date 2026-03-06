using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

#pragma warning disable CS1591 // McpTools use [Description] attribute instead of XML doc comments

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for RoutedEvent tools (3 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class EventMcpTools
{
    [McpServerTool(Name = "trace_routed_events", OpenWorld = false, ReadOnly = true)]
    [Description(
        "[Event] Trace a routed event over a short capture window and return the collected event records. " +
        "Use this to see whether a routed event fired, how many records were captured, and the handled state of each record.\n\n" +
        "USE WHEN: Debugging event handling issues; confirming whether Click/MouseDown style events are firing.\n" +
        "DO NOT USE: As a long-running subscription. This tool blocks until the capture window completes and then returns the collected records.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  eventName: string,\n" +
        "  duration: integer,\n" +
        "  eventCount: integer,\n" +
        "  events: [{\n" +
        "    timestamp, sender, routingStrategy, handled\n" +
        "  }]\n" +
        "}\n\n" +
        "TIP: Keep durationMs small (250-2000) so the trace completes quickly in STDIO clients.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"invalid event name\" -> verify eventName is a valid WPF RoutedEvent\n" +
        "- \"eventName required\" -> must specify which event to trace\n\n" +
        "Examples:\n" +
        "- { processId: 12345, eventName: \"MouseDown\", durationMs: 500 }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", eventName: \"Click\", durationMs: 1000 }")]
    public static Task<CallToolResult> TraceRoutedEvents(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("WPF routed event name to trace, such as Click or MouseDown.")] string eventName,
        [Description("Optional element ID to scope the event trace. Omit for the root window.")] string? elementId = null,
        [Description("Optional capture window in milliseconds. Use smaller values for interactive STDIO sessions.")] int? durationMs = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName),
            ("duration", durationMs));

        var timeoutSeconds = Math.Max(
            McpServerConfiguration.DefaultToolTimeoutSeconds,
            (int)Math.Ceiling(((durationMs ?? 5000) / 1000d) + 2));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<TraceRoutedEventsTool>("TraceRoutedEventsTool", () => new TraceRoutedEventsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            timeoutSeconds: timeoutSeconds);
    }

    [McpServerTool(Name = "get_event_handlers", OpenWorld = false, ReadOnly = true)]
    [Description(
        "[Event] Get all event handlers attached to a WPF element for a specific routed event. " +
        "Returns handler method names, declaring types, and whether they handle tunneling/bubbling.\n\n" +
        "USE WHEN: Button click does nothing; need to verify event handlers are attached.\n" +
        "DO NOT USE: Without eventName - it's required.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  handlers: [{\n" +
        "    methodName, declaringType, handledEventsToo: boolean\n" +
        "  }]\n" +
        "}\n\n" +
        "Empty handlers array means no handlers attached.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid event name\" -> verify eventName is valid\n" +
        "- \"eventName required\" -> must specify which event\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", eventName: \"Click\" }")]
    public static Task<CallToolResult> GetEventHandlers(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("WPF routed event name whose handlers should be listed.")] string eventName,
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

    [McpServerTool(Name = "fire_routed_event", OpenWorld = false, Destructive = true)]
    [Description(
        "[Event] Fire a routed event on a WPF element. " +
        "Triggers the full WPF routed event pipeline (Tunneling -> Direct -> Bubbling).\n\n" +
        "USE WHEN: Testing event handlers programmatically; simulating user interactions.\n" +
        "DO NOT USE: For mouse clicks (use click_element instead); for keyboard input (use simulate_keyboard).\n\n" +
        "WARNING: This triggers real application logic.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  fired: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"invalid event name\" -> verify eventName is valid\n" +
        "- \"eventName required\" -> must specify which event\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", eventName: \"Click\" }")]
    public static Task<CallToolResult> FireRoutedEvent(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("WPF routed event name to raise, such as Click.")] string eventName,
        [Description("Optional target element ID that should receive the routed event.")] string? elementId = null,
        [Description("Optional JSON payload for custom routed event arguments. Currently unused for standard RoutedEvents (Click, MouseDown); reserved for custom events.")] JsonElement? eventArgs = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName),
            ("eventArgs", eventArgs));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<FireRoutedEventTool>("FireRoutedEventTool", () => new FireRoutedEventTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
