using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Tree and XAML tools registration (6 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 2. Tree & XAML (6 tools) ===
    private static void RegisterTreeTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_visual_tree",
            "[Tree] Get the Visual Tree (rendering structure) of a WPF element. Returns a hierarchical tree with elementId, type, name, and children for each node.\n\n" +
            "USE WHEN: You need to inspect template-generated elements, adorners, or the actual rendering structure.\n" +
            "DO NOT USE: Without depth parameter on large apps (use depth=2-4); use get_logical_tree for XAML structure only.\n\n" +
            "⚠️ PERFORMANCE: Large trees (depth >5) can return 10,000+ elements. Always set depth=2-4 for initial exploration.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  tree: { elementId, type, name, childCount, children: [...] },\n" +
            "  totalElements: integer\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId from previous get_visual_tree call",
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
                    depth = new {
                        type = "integer",
                        description = "Maximum tree traversal depth. Use 2-4 for large apps to limit response size.",
                        minimum = 1,
                        maximum = 100,
                        @default = 10
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new GetVisualTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, depth = 3 },
                new { processId = 12345, elementId = "NameTextBox", depth = 2 }
            });

        RegisterTool(registry, "get_logical_tree",
            "[Tree] Get the Logical Tree (semantic/XAML structure) of a WPF element. Simpler than Visual Tree - shows only elements defined in XAML.\n\n" +
            "USE WHEN: You need to understand XAML structure, find named elements, or trace DataContext inheritance.\n" +
            "DO NOT USE: When you need to inspect template internals (use get_visual_tree or get_template_tree instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  tree: { elementId, type, name, childCount, children: [...] }\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId is valid",
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
                    depth = new {
                        type = "integer",
                        description = "Maximum tree traversal depth. Use 2-4 for large apps.",
                        minimum = 1,
                        maximum = 100,
                        @default = 10
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new GetLogicalTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, depth = 5 }
            });

        RegisterTool(registry, "serialize_to_xaml",
            "[Tree] Serialize a WPF element to its XAML representation. Returns the XAML markup string for the element and its children.\n\n" +
            "USE WHEN: You need to understand element structure in markup form or export UI definition.\n" +
            "DO NOT USE: On large subtrees (use elementId to scope to specific element).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  xaml: string\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"serialization failed\" → element may contain non-serializable properties",
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
            async (args, ct) => await new GenericPipeTool(sessionManager, "serialize_to_xaml").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_namescope",
            "[Tree] Get the XAML NameScope of a WPF element. Returns all named elements (x:Name) registered in the element's scope.\n\n" +
            "USE WHEN: You need to discover all named elements in a window or UserControl.\n" +
            "DO NOT USE: For finding elements by type (use get_visual_tree with filter instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  names: [{ name, elementId, type }]\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"no namescope\" → element is not a namescope root (try parent window)",
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
            async (args, ct) => await new GenericPipeTool(sessionManager, "get_namescope").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_template_tree",
            "[Tree] Get the template Visual Tree of a templated WPF control (Button, ListBox, etc.). Shows the internal rendering structure defined by the control's ControlTemplate.\n\n" +
            "USE WHEN: You need to inspect how a control renders internally or find template parts.\n" +
            "DO NOT USE: On non-templated elements (will return empty); check element type first.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  tree: { elementId, type, name, childCount, children: [...] }\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"no template\" → element is not a templated control\n" +
            "- \"elementId required\" → must specify which control to inspect",
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
                        description = "Element ID of the templated control (obtained from get_visual_tree or get_logical_tree). REQUIRED for this tool."
                    },
                    depth = new {
                        type = "integer",
                        description = "Maximum tree traversal depth.",
                        minimum = 1,
                        maximum = 100,
                        @default = 10
                    }
                },
                required = new[] { "processId", "elementId" }
            },
            async (args, ct) => await new GetTemplateTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "compare_trees",
            "[Tree] Compare Visual and Logical trees to identify structural differences. Returns elements present in one tree but not the other.\n\n" +
            "USE WHEN: You need to understand which elements are template-generated vs XAML-defined.\n" +
            "DO NOT USE: On large apps without elementId scope (will be slow).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  onlyInVisual: [{ elementId, type }],\n" +
            "  onlyInLogical: [{ elementId, type }]\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first",
            new {
                type = "object",
                additionalProperties = false,
                properties = new {
                    processId = new {
                        type = "integer",
                        description = "Process ID of the connected WPF application (from get_processes)"
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new GenericPipeTool(sessionManager, "compare_trees").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });
    }
}
