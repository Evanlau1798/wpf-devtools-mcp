using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Layout tools registration (4 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 8. Layout (4 tools) ===
    private static void RegisterLayoutTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_layout_info",
            "[Layout] Get layout information of a WPF element. Returns: actualWidth, actualHeight, desiredSize, renderSize, margin, padding, horizontalAlignment, verticalAlignment, position relative to parent and window.\n\n" +
            "USE WHEN: Element has wrong size, position, or alignment; debugging layout issues.\n" +
            "DO NOT USE: For clipping issues (use get_clipping_info instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  actualWidth, actualHeight, desiredWidth, desiredHeight,\n" +
            "  margin: { left, top, right, bottom },\n" +
            "  padding: { left, top, right, bottom },\n" +
            "  horizontalAlignment, verticalAlignment,\n" +
            "  positionInParent: { x, y }, positionInWindow: { x, y }\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId",
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
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new GetLayoutInfoTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_clipping_info",
            "[Layout] Get clipping information of a WPF element. Returns whether the element is clipped by any ancestor, the clip bounds, and how much content overflows.\n\n" +
            "USE WHEN: Element appears cut off or partially hidden; debugging ScrollViewer issues.\n" +
            "DO NOT USE: For general layout info (use get_layout_info instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  isClipped: boolean,\n" +
            "  clipBounds: { x, y, width, height },\n" +
            "  overflowAmount: { left, top, right, bottom }\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"elementId required\" → must specify which element to check",
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
                        description = "Element ID obtained from get_visual_tree or get_logical_tree. REQUIRED for this tool."
                    }
                },
                required = new[] { "processId", "elementId" }
            },
            async (args, ct) => await new GetClippingInfoTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox" }
            });

        RegisterTool(registry, "highlight_element",
            "[Layout] Visually highlight an element with a colored border overlay. Useful for confirming you have the right element. Color accepts WPF color names ('Red', 'Blue', 'Yellow') or hex. Auto-removes after duration.\n\n" +
            "USE WHEN: Verifying element identification; showing users which element you're inspecting.\n" +
            "DO NOT USE: On collapsed or zero-size elements (won't be visible).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  highlighted: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"invalid color\" → use WPF color names or hex format",
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
                    color = new {
                        type = "string",
                        description = "WPF color name ('Red','Blue','Yellow','Green','Orange') or hex (#AARRGGBB or #RRGGBB).",
                        @default = "Red",
                        @enum = new[] { "Red", "Blue", "Yellow", "Green", "Orange", "Purple", "Cyan", "Magenta" }
                    },
                    duration = new {
                        type = "integer",
                        description = "Duration in milliseconds before auto-removing the highlight.",
                        minimum = 100,
                        maximum = 60000,
                        @default = 2000
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new GenericPipeTool(sessionManager, "highlight_element",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var color = ParameterParser.ParseStringParam(a, "color");
                    var duration = ParameterParser.ParseIntParam(a, "duration");
                    return (pid, (object?)new { elementId = eid, color, duration }, null);
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345, elementId = "SaveButton", color = "Red", duration = 3000 }
            });

        RegisterTool(registry, "invalidate_layout",
            "[Layout] Force layout invalidation on a WPF element, causing it to re-measure and re-arrange. Use after modifying properties that affect layout to force an immediate update.\n\n" +
            "USE WHEN: Layout doesn't update after property changes; testing layout behavior.\n" +
            "DO NOT USE: Repeatedly in a loop (causes performance issues).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  invalidated: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId",
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
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new InvalidateLayoutTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" }
            });
    }
}
