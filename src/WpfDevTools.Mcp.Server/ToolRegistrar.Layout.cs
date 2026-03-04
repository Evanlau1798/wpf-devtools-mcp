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
            "[Layout] Get layout information of a WPF element. Returns: actualWidth, actualHeight, desiredSize, renderSize, margin, padding, horizontalAlignment, verticalAlignment, position relative to parent and window.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetLayoutInfoTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_clipping_info",
            "[Layout] Get clipping information of a WPF element. Returns whether the element is clipped by any ancestor, the clip bounds, and how much content overflows. Useful for debugging elements that appear cut off. Returns: { isClipped, clipBounds, overflowAmount }",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetClippingInfoTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox" }
            });

        RegisterTool(registry, "highlight_element",
            "[Layout] Visually highlight an element with a colored border overlay. Useful for confirming you have the right element. Color accepts WPF color names ('Red', 'Blue', 'Yellow') or hex. Auto-removes after duration.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, color = new { type = "string", description = "WPF color name ('Red','Blue','Yellow') or hex (#AARRGGBB). Default: 'Red'" }, duration = new { type = "integer", description = "Duration in milliseconds before auto-removing the highlight. Default: 2000" } }, required = new[] { "processId" } },
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
            "[Layout] Force layout invalidation on a WPF element, causing it to re-measure and re-arrange. Use after modifying properties that affect layout to force an immediate update.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new InvalidateLayoutTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" }
            });
    }
}
