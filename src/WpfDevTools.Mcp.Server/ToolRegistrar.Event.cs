using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// RoutedEvent tools registration (3 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 6. RoutedEvent (3 tools) ===
    private static void RegisterEventTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "trace_routed_events",
            "[Event] Start tracing a routed event's propagation path (Tunneling -> Direct -> Bubbling). Returns trace data showing which elements the event passes through. NOTE: Event push requires HTTP+SSE transport (planned).",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, eventName = new { type = "string", description = "WPF RoutedEvent name, e.g., 'ButtonBase.Click', 'UIElement.MouseDown'" } }, required = new[] { "processId", "eventName" } },
            async (args, ct) => await new TraceRoutedEventsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, eventName = "MouseDown" },
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });

        RegisterTool(registry, "get_event_handlers",
            "[Event] Get all event handlers attached to a WPF element for a specific routed event. Returns handler method names, declaring types, and whether they handle tunneling/bubbling. Use to check why a button click does nothing.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, eventName = new { type = "string", description = "WPF RoutedEvent name, e.g., 'ButtonBase.Click', 'UIElement.MouseDown'" } }, required = new[] { "processId", "eventName" } },
            async (args, ct) => await new GetEventHandlersTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });

        RegisterTool(registry, "fire_routed_event",
            "[Event] Fire a routed event on a WPF element. Triggers the full WPF routed event pipeline (Tunneling -> Direct -> Bubbling). WARNING: This triggers real application logic.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, eventName = new { type = "string", description = "WPF RoutedEvent name, e.g., 'ButtonBase.Click', 'UIElement.MouseDown'" } }, required = new[] { "processId", "eventName" } },
            async (args, ct) => await new FireRoutedEventTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });
    }
}
