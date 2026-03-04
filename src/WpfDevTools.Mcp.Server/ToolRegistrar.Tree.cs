using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Tree & XAML tools registration (6 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 2. Tree & XAML (6 tools) ===
    private static void RegisterTreeTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_visual_tree",
            "[Tree] Get the Visual Tree (rendering structure) of a WPF element. Returns a hierarchical tree with elementId, type, name, and children for each node. Use elementId from the response in other tools. Use depth=2-4 for large apps to limit response size.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, depth = new { type = "integer", description = "Maximum tree traversal depth (1-100). Use 2-4 for large apps. Default varies by tool." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetVisualTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, depth = 3 },
                new { processId = 12345, elementId = "NameTextBox", depth = 2 }
            });

        RegisterTool(registry, "get_logical_tree",
            "[Tree] Get the Logical Tree (semantic/XAML structure) of a WPF element. Simpler than Visual Tree - shows only elements defined in XAML. Returns elementId, type, name, childCount, and children.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, depth = new { type = "integer", description = "Maximum tree traversal depth (1-100). Use 2-4 for large apps. Default varies by tool." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetLogicalTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, depth = 5 }
            });

        RegisterTool(registry, "serialize_to_xaml",
            "[Tree] Serialize a WPF element to its XAML representation. Returns the XAML markup string for the element and its children. Useful for understanding how elements are structured in markup.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "serialize_to_xaml").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_namescope",
            "[Tree] Get the XAML NameScope of a WPF element. Returns all named elements (x:Name) registered in the element's scope. Useful for discovering elements by name.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "get_namescope").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_template_tree",
            "[Tree] Get the template Visual Tree of a templated WPF control (Button, ListBox, etc.). Shows the internal rendering structure defined by the control's ControlTemplate. Useful for understanding how a control renders internally.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, depth = new { type = "integer", description = "Maximum tree traversal depth (1-100). Use 2-4 for large apps. Default varies by tool." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetTemplateTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "compare_trees",
            "[Tree] Compare Visual and Logical trees to identify structural differences. Returns elements present in one tree but not the other. Useful for understanding template-generated elements vs XAML-defined elements.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "compare_trees").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });
    }
}
