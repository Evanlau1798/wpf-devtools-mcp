using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Interaction tools registration (5 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 7. Interaction (5 tools) ===
    private static void RegisterInteractionTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "click_element",
            "[Interaction] Simulate a mouse click on a WPF element. Raises the full WPF click event pipeline. WARNING: This triggers real application logic (e.g., button handlers, navigation).",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new ClickElementTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345, elementId = "ClearButton" }
            });

        RegisterTool(registry, "drag_and_drop",
            "[Interaction] Simulate drag and drop between two WPF elements. Raises DragEnter, DragOver, and Drop events on the target. WARNING: This triggers real application logic.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, sourceElementId = new { type = "string", description = "Element ID of the drag source (obtained from get_visual_tree or get_logical_tree)" }, targetElementId = new { type = "string", description = "Element ID of the drop target (obtained from get_visual_tree or get_logical_tree)" } }, required = new[] { "processId", "sourceElementId", "targetElementId" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "drag_and_drop",
                a =>
                {
                    var (pid, _, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var sourceElementId = ParameterParser.ParseStringParam(a, "sourceElementId");
                    var targetElementId = ParameterParser.ParseStringParam(a, "targetElementId");
                    if (string.IsNullOrEmpty(sourceElementId)) return (-1, null, (object)new { success = false, error = "Missing required parameter: sourceElementId" });
                    if (string.IsNullOrEmpty(targetElementId)) return (-1, null, (object)new { success = false, error = "Missing required parameter: targetElementId" });
                    return (pid, (object?)new { sourceElementId, targetElementId }, null);
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, sourceElementId = "Item1", targetElementId = "Item2" }
            });

        RegisterTool(registry, "scroll_to_element",
            "[Interaction] Scroll a WPF element into view within its parent ScrollViewer. Calls BringIntoView() on the element. Use before element_screenshot to ensure visibility.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new ScrollToElementTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox" }
            });

        RegisterTool(registry, "simulate_keyboard",
            "[Interaction] Simulate a keyboard key press on an element. Key parameter uses WPF Key enum names: 'Enter', 'Tab', 'Escape', 'Back', 'A'-'Z', 'F1'-'F12', etc. WARNING: This triggers real application logic.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, key = new { type = "string", description = "WPF Key enum name: 'Enter', 'Tab', 'Escape', 'Back', 'A'-'Z', 'F1'-'F12', etc." } }, required = new[] { "processId", "key" } },
            async (args, ct) => await new SimulateKeyboardTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", key = "Enter" },
                new { processId = 12345, elementId = "NameTextBox", key = "Tab" }
            });

        RegisterTool(registry, "element_screenshot",
            "[Interaction] Capture a PNG screenshot of a specific element. Returns base64-encoded image data. The screenshot is taken on the TARGET MACHINE running the WPF app.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, outputPath = new { type = "string", description = "Optional file path to save screenshot on the target machine. If omitted, returns base64 data." } }, required = new[] { "processId" } },
            async (args, ct) => await new ElementScreenshotTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });
    }
}
