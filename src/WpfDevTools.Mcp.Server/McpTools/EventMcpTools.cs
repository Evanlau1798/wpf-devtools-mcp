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
    [McpServerTool(Name = "trace_routed_events", ReadOnly = true)]
    [Description(
        "[Event] Start tracing a routed event's propagation path " +
        "(Tunneling -> Direct -> Bubbling). Returns trace data showing which elements the event passes through.\n\n" +
        "USE WHEN: Debugging event handling issues; understanding why an event is handled/not handled.\n" +
        "DO NOT USE: In STDIO mode expecting real-time push (events require HTTP+SSE transport).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  trace: [{\n" +
        "    phase: 'Tunneling'|'Direct'|'Bubbling',\n" +
        "    elementId, elementType, handled: boolean\n" +
        "  }]\n" +
        "}\n\n" +
        "NOTE: Event push requires HTTP+SSE transport (planned Phase 2+).\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"invalid event name\" -> verify eventName is a valid WPF RoutedEvent\n" +
        "- \"eventName required\" -> must specify which event to trace\n\n" +
        "Examples:\n" +
        "- { processId: 12345, eventName: \"MouseDown\" }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", eventName: \"Click\" }")]
    public static Task<CallToolResult> TraceRoutedEvents(
        SessionManager sessionManager,
        int processId,
        string eventName,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<TraceRoutedEventsTool>("TraceRoutedEventsTool", () => new TraceRoutedEventsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_event_handlers", ReadOnly = true)]
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
        int processId,
        string eventName,
        string? elementId = null,
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

    [McpServerTool(Name = "fire_routed_event", Destructive = true)]
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
        int processId,
        string eventName,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<FireRoutedEventTool>("FireRoutedEventTool", () => new FireRoutedEventTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
