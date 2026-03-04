using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Style/Template tools registration (4 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 5. Style/Template (4 tools) ===
    private static void RegisterStyleTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_applied_styles",
            "[Style] Get all applied styles on a WPF element. Returns style type, target type, setters (property+value), and whether it's an implicit or explicit style. Use to understand why an element looks a certain way.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetAppliedStylesTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_triggers",
            "[Style] Get all triggers from a WPF element's styles and templates. Returns trigger type (Property/Data/Event/MultiTrigger), conditions, and setter actions. Useful for debugging conditional styling. Returns: { triggers: [{ triggerType, conditions, setters }] }",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetTriggersTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_resource_chain",
            "[Style] Get the resource lookup chain for a XAML resource key. Shows which ResourceDictionary at which level (element, window, app, theme) provides the resource. Essential for debugging 'resource not found' issues. Returns: { chain: [{ level, dictionarySource, value }], found }",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, resourceKey = new { type = "string", description = "XAML resource key to look up in the resource chain" } }, required = new[] { "processId", "resourceKey" } },
            async (args, ct) => await new GetResourceChainTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, resourceKey = "PrimaryBrush" },
                new { processId = 12345, elementId = "SaveButton", resourceKey = "ButtonStyle" }
            });

        RegisterTool(registry, "override_style_setter",
            "[Style] Override a style setter value on a WPF element at runtime. Applies a local value that takes precedence over the style. Changes are not persisted. WARNING: modifies the running app.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the property to override (e.g., 'Background', 'Foreground', 'FontSize')" }, value = new { type = "string", description = "String representation of value. Auto-converted to property type. Examples: 'Red', '#FF0000', '14'" } }, required = new[] { "processId", "propertyName", "value" } },
            async (args, ct) => await new OverrideStyleSetterTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "Background", value = "Red" }
            });
    }
}
