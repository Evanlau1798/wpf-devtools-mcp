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
            "[Style] Get all applied styles on a WPF element. Returns style type, target type, setters (property+value), and whether it's an implicit or explicit style.\n\n" +
            "USE WHEN: Element has unexpected appearance; need to understand which styles are applied.\n" +
            "DO NOT USE: For runtime property values (use get_dp_value_source instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  styles: [{\n" +
            "    styleType: 'Implicit'|'Explicit',\n" +
            "    targetType,\n" +
            "    setters: [{ property, value }]\n" +
            "  }]\n" +
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
            async (args, ct) => await new GetAppliedStylesTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_triggers",
            "[Style] Get all triggers from a WPF element's styles and templates. Returns trigger type (Property/Data/Event/MultiTrigger), conditions, and setter actions.\n\n" +
            "USE WHEN: Conditional styling not working; need to understand trigger logic.\n" +
            "DO NOT USE: For static styles (use get_applied_styles instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  triggers: [{\n" +
            "    triggerType: 'Property'|'Data'|'Event'|'MultiTrigger',\n" +
            "    conditions: [{ property, value }],\n" +
            "    setters: [{ property, value }]\n" +
            "  }]\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"elementId required\" → must specify which element",
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
            async (args, ct) => await new GetTriggersTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_resource_chain",
            "[Style] Get the resource lookup chain for a XAML resource key. Shows which ResourceDictionary at which level (element, window, app, theme) provides the resource.\n\n" +
            "USE WHEN: Resource not found errors; need to understand resource lookup order.\n" +
            "DO NOT USE: Without resourceKey - it's required.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  found: boolean,\n" +
            "  chain: [{\n" +
            "    level: 'Element'|'Window'|'Application'|'Theme',\n" +
            "    dictionarySource, value\n" +
            "  }]\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"resourceKey required\" → must specify which resource to look up",
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
                    resourceKey = new {
                        type = "string",
                        description = "XAML resource key to look up in the resource chain (e.g., 'PrimaryBrush', 'ButtonStyle')"
                    }
                },
                required = new[] { "processId", "resourceKey" }
            },
            async (args, ct) => await new GetResourceChainTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, resourceKey = "PrimaryBrush" },
                new { processId = 12345, elementId = "SaveButton", resourceKey = "ButtonStyle" }
            });

        RegisterTool(registry, "override_style_setter",
            "[Style] Override a style setter value on a WPF element at runtime. Applies a local value that takes precedence over the style.\n\n" +
            "USE WHEN: Testing different style values; debugging style precedence issues.\n" +
            "DO NOT USE: For permanent changes (not persisted to XAML).\n\n" +
            "⚠️ WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  propertyName, oldValue, newValue\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId\n" +
            "- \"property not found\" → verify propertyName is valid\n" +
            "- \"conversion failed\" → value cannot be converted to property type\n" +
            "- \"propertyName required\" → must specify which property\n" +
            "- \"value required\" → must provide new value",
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
                    propertyName = new {
                        type = "string",
                        description = "Name of the property to override (e.g., 'Background', 'Foreground', 'FontSize')"
                    },
                    value = new {
                        type = "string",
                        description = "String representation of value. Auto-converted to property type. Examples: 'Red', '#FF0000', '14'"
                    }
                },
                required = new[] { "processId", "propertyName", "value" }
            },
            async (args, ct) => await new OverrideStyleSetterTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "Background", value = "Red" }
            });
    }
}
