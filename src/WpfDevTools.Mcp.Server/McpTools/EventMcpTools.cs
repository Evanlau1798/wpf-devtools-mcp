using System.Text.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for RoutedEvent tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class EventMcpTools
{

    [McpServerTool(Name = "trace_routed_events", Title = "Trace WPF Routed Events", OpenWorld = false, ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(EventMcpToolDescriptions.TraceRoutedEvents)]
    public static Task<CallToolResult> TraceRoutedEvents(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional WPF routed event name to trace, such as Click or MouseDown. Required for `capture` and `start`, optional for `get`.")] string? eventName = null,
        [Description("Optional element ID to scope the event trace. Omit for the root window.")] string? elementId = null,
        [Range(0, TraceRoutedEventsTool.MaxDurationMs)]
        [Description("Optional capture window in milliseconds (default: 5000, maximum: 60000). Use smaller values (250-2000) for interactive STDIO sessions.")] int? durationMs = null,
        [AllowedValues("capture", "start", "get")]
        [Description("Optional tracing mode: `capture` (default), `start`, or `get`. Use `start` + `get` for AI-friendly non-blocking workflows.")] string? mode = null,
        [Description("Optional opt-in override for start mode. When true, short requested durations are honored instead of being raised to the default 30s minimum.")] bool allowShortStartDuration = false,
        [Description("Optional positive cap on returned trace event records. Responses include returnedEventCount, totalEventCount, eventsTruncated, and maxEvents so agents can detect truncation and retry with a larger cap when needed.")] int? maxEvents = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName),
            ("duration", durationMs),
            ("mode", mode),
            ("allowShortStartDuration", allowShortStartDuration),
            ("maxEvents", maxEvents));

        var boundedDurationMs = Math.Min(durationMs ?? 5000, TraceRoutedEventsTool.MaxDurationMs);
        var timeoutSeconds = Math.Max(
            McpServerConfiguration.DefaultToolTimeoutSeconds,
            (int)Math.Ceiling((boundedDurationMs / 1000d) + 2));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<TraceRoutedEventsTool>(sessionManager, "TraceRoutedEventsTool", () => new TraceRoutedEventsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            timeoutSeconds: timeoutSeconds);
    }

    [McpServerTool(Name = "get_event_handlers", Title = "Inspect WPF Event Handlers", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(EventMcpToolDescriptions.GetEventHandlers)]
    public static Task<CallToolResult> GetEventHandlers(
        SessionManager sessionManager,
        [Description("WPF routed event name whose handlers should be listed.")] string eventName,
        [Description("Required element ID whose handlers should be inspected.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetEventHandlersTool>(sessionManager, "GetEventHandlersTool", () => new GetEventHandlersTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_event_handlers");
    }

    [McpServerTool(Name = "fire_routed_event", Title = "Fire WPF Routed Event", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(EventMcpToolDescriptions.FireRoutedEvent)]
    public static Task<CallToolResult> FireRoutedEvent(
        SessionManager sessionManager,
        [Description("WPF routed event name to raise, such as Click.")] string eventName,
        [Description("Required target element ID that should receive the routed event.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional JSON payload for custom routed event arguments. Currently unused for standard RoutedEvents (Click, MouseDown); reserved for custom events.")] JsonElement? eventArgs = null,
        [AllowedValues("compact", "minimal", "verbose", "standard")]
        [Description("Optional metadata detail mode: omit or use 'compact' (default), use 'minimal' for success/property/newValue confirmation only, use 'verbose' for full additive metadata, or 'standard' as a compatibility alias.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("eventName", eventName),
            ("eventArgs", eventArgs),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<FireRoutedEventTool>(sessionManager, "FireRoutedEventTool", () => new FireRoutedEventTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "fire_routed_event");
    }
}
