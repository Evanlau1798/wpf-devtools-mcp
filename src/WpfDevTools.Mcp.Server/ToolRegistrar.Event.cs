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
            "[Event] Start tracing a routed event's propagation path (Tunneling -> Direct -> Bubbling). Returns trace data showing which elements the event passes through.\n\n" +
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
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"invalid event name\" → verify eventName is a valid WPF RoutedEvent\n" +
            "- \"eventName required\" → must specify which event to trace",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    },
                    elementId = new {
                        type = "string",
                        description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window.",
                        @default = (object?)null
                    },
                    eventName = new {
                        type = "string",
                        description = "WPF RoutedEvent name. Common values: 'Click', 'MouseDown', 'MouseUp', 'KeyDown', 'KeyUp', 'GotFocus', 'LostFocus', 'Loaded', 'Unloaded'",
                        @enum = new[] {
                            "Click", "MouseDown", "MouseUp", "MouseEnter", "MouseLeave", "MouseMove",
                            "KeyDown", "KeyUp", "GotFocus", "LostFocus",
                            "Loaded", "Unloaded", "SizeChanged",
                            "PreviewMouseDown", "PreviewMouseUp", "PreviewKeyDown", "PreviewKeyUp"
                        }
                    }
                },
                required = new[] { "processId", "eventName" }
            },
            async (args, ct) => await new TraceRoutedEventsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, eventName = "MouseDown" },
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });

        RegisterTool(registry, "get_event_handlers",
            "[Event] Get all event handlers attached to a WPF element for a specific routed event. Returns handler method names, declaring types, and whether they handle tunneling/bubbling.\n\n" +
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
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"invalid event name\" → verify eventName is valid\n" +
            "- \"eventName required\" → must specify which event",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    },
                    elementId = new {
                        type = "string",
                        description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window.",
                        @default = (object?)null
                    },
                    eventName = new {
                        type = "string",
                        description = "WPF RoutedEvent name. Common values: 'Click', 'MouseDown', 'KeyDown', 'GotFocus'",
                        @enum = new[] {
                            "Click", "MouseDown", "MouseUp", "MouseEnter", "MouseLeave",
                            "KeyDown", "KeyUp", "GotFocus", "LostFocus",
                            "Loaded", "Unloaded"
                        }
                    }
                },
                required = new[] { "processId", "eventName" }
            },
            async (args, ct) => await new GetEventHandlersTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });

        RegisterTool(registry, "fire_routed_event",
            "[Event] Fire a routed event on a WPF element. Triggers the full WPF routed event pipeline (Tunneling -> Direct -> Bubbling).\n\n" +
            "USE WHEN: Testing event handlers programmatically; simulating user interactions.\n" +
            "DO NOT USE: For mouse clicks (use click_element instead); for keyboard input (use simulate_keyboard).\n\n" +
            "⚠️ WARNING: This triggers real application logic.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  fired: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"invalid event name\" → verify eventName is valid\n" +
            "- \"eventName required\" → must specify which event",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    },
                    elementId = new {
                        type = "string",
                        description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window.",
                        @default = (object?)null
                    },
                    eventName = new {
                        type = "string",
                        description = "WPF RoutedEvent name to fire",
                        @enum = new[] {
                            "Click", "MouseDown", "MouseUp", "MouseEnter", "MouseLeave",
                            "KeyDown", "KeyUp", "GotFocus", "LostFocus",
                            "Loaded", "Unloaded"
                        }
                    }
                },
                required = new[] { "processId", "eventName" }
            },
            async (args, ct) => await new FireRoutedEventTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });
    }
}
