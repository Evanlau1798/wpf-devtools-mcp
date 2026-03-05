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
            "[Interaction] Simulate a mouse click on a WPF element. Raises the full WPF click event pipeline.\n\n" +
            "USE WHEN: Testing button handlers, navigation, or click-triggered logic.\n" +
            "DO NOT USE: On disabled elements (check IsEnabled first with get_dp_value_source).\n\n" +
            "⚠️ WARNING: This triggers real application logic (e.g., button handlers, navigation, data modifications).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  clicked: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId from get_visual_tree\n" +
            "- \"element not clickable\" → element is disabled or not a clickable type",
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
            async (args, ct) => await new ClickElementTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345, elementId = "ClearButton" }
            });

        RegisterTool(registry, "drag_and_drop",
            "[Interaction] Simulate drag and drop between two WPF elements. Raises DragEnter, DragOver, and Drop events on the target.\n\n" +
            "USE WHEN: Testing drag-drop functionality, reordering items, or file drop handlers.\n" +
            "DO NOT USE: Without verifying both elements exist first.\n\n" +
            "⚠️ WARNING: This triggers real application logic.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  dropped: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"source not found\" → verify sourceElementId\n" +
            "- \"target not found\" → verify targetElementId\n" +
            "- \"sourceElementId required\" → must specify drag source\n" +
            "- \"targetElementId required\" → must specify drop target",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    },
                    sourceElementId = new {
                        type = "string",
                        description = "Element ID of the drag source (obtained from get_visual_tree or get_logical_tree)"
                    },
                    targetElementId = new {
                        type = "string",
                        description = "Element ID of the drop target (obtained from get_visual_tree or get_logical_tree)"
                    }
                },
                required = new[] { "processId", "sourceElementId", "targetElementId" }
            },
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
            "[Interaction] Scroll a WPF element into view within its parent ScrollViewer. Calls BringIntoView() on the element.\n\n" +
            "USE WHEN: Element is off-screen before taking screenshot or clicking; testing scroll behavior.\n" +
            "DO NOT USE: On elements not inside a ScrollViewer (has no effect).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  scrolled: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"elementId required\" → must specify which element to scroll to",
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
            async (args, ct) => await new ScrollToElementTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox" }
            });

        RegisterTool(registry, "simulate_keyboard",
            "[Interaction] Simulate a keyboard key press on an element. Key parameter uses WPF Key enum names.\n\n" +
            "USE WHEN: Testing keyboard shortcuts, Enter key submission, Tab navigation, or key event handlers.\n" +
            "DO NOT USE: For text input (use set_dp_value on Text property instead).\n\n" +
            "⚠️ WARNING: This triggers real application logic.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  keyPressed: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"invalid key\" → key name not recognized (use WPF Key enum names)\n" +
            "- \"key required\" → must specify which key to press",
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
                    key = new {
                        type = "string",
                        description = "WPF Key enum name. Common values: 'Enter', 'Tab', 'Escape', 'Back', 'Delete', 'Space', 'A'-'Z', 'D0'-'D9', 'F1'-'F12', 'Left', 'Right', 'Up', 'Down'",
                        @enum = new[] {
                            "Enter", "Tab", "Escape", "Back", "Delete", "Space",
                            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
                            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
                            "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
                            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
                            "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown"
                        }
                    }
                },
                required = new[] { "processId", "key" }
            },
            async (args, ct) => await new SimulateKeyboardTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", key = "Enter" },
                new { processId = 12345, elementId = "NameTextBox", key = "Tab" }
            });

        RegisterTool(registry, "element_screenshot",
            "[Interaction] Capture a PNG screenshot of a specific element. Returns base64-encoded image data. The screenshot is taken on the TARGET MACHINE running the WPF app.\n\n" +
            "USE WHEN: Visual verification needed; documenting UI state; debugging rendering issues.\n" +
            "DO NOT USE: On off-screen elements (use scroll_to_element first).\n\n" +
            "⚠️ PERFORMANCE: Large elements produce large base64 strings. Use outputPath for big screenshots.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  base64Image: string (if no outputPath),\n" +
            "  filePath: string (if outputPath specified)\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"render failed\" → element may be collapsed or have zero size",
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
                    outputPath = new {
                        type = "string",
                        description = "Optional file path to save screenshot on the target machine. If omitted, returns base64 data.",
                        @default = (object?)null
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new ElementScreenshotTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });
    }
}
