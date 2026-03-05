using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// DependencyProperty tools registration (5 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 4. DependencyProperty (5 tools) ===
    private static void RegisterDependencyPropertyTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_dp_value_source",
            "[DependencyProperty] Get the value source of a DependencyProperty. Returns where the current value comes from: Default, Inherited, Style, Trigger, TemplateBinding, LocalValue, or Animation.\n\n" +
            "USE WHEN: Property has unexpected value; need to understand precedence (Style vs LocalValue vs Animation).\n" +
            "DO NOT USE: Without propertyName - it's required.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  propertyName, currentValue, valueSource: 'Default'|'Inherited'|'Style'|'LocalValue'|'Trigger'|'Animation',\n" +
            "  isAnimated, isCoerced\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"property not found\" → verify propertyName is a valid DependencyProperty\n" +
            "- \"propertyName required\" → must specify which property",
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
                        description = "Name of the DependencyProperty to check (e.g., 'IsEnabled', 'Visibility', 'Text')"
                    }
                },
                required = new[] { "processId", "propertyName" }
            },
            async (args, ct) => await new GetDpValueSourceTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" },
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" }
            });

        RegisterTool(registry, "get_dp_metadata",
            "[DependencyProperty] Get DependencyProperty metadata including default value, inherits flag, affects measure/arrange, and coerce/validation callbacks.\n\n" +
            "USE WHEN: You need to understand property behavior at framework level (inheritance, layout impact).\n" +
            "DO NOT USE: For runtime value inspection (use get_dp_value_source instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  propertyName, defaultValue, inherits, affectsMeasure, affectsArrange,\n" +
            "  hasCoerceCallback, hasValidationCallback\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"property not found\" → verify propertyName is a valid DependencyProperty",
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
                        description = "Name of the DependencyProperty to get metadata for (e.g., 'IsEnabled', 'Visibility')"
                    }
                },
                required = new[] { "processId", "propertyName" }
            },
            async (args, ct) => await new GetDpMetadataTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, propertyName = "IsEnabled" },
                new { processId = 12345, propertyName = "Visibility" }
            });

        RegisterTool(registry, "set_dp_value",
            "[DependencyProperty] Set a DependencyProperty value at runtime. Value is a string that gets type-converted.\n\n" +
            "USE WHEN: Testing UI behavior with different property values; debugging layout/styling issues.\n" +
            "DO NOT USE: For permanent changes (changes are NOT persisted to XAML).\n\n" +
            "⚠️ WARNING: This modifies the running app. Changes are lost on app restart.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  propertyName, oldValue, newValue\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"property not found\" → verify propertyName is valid\n" +
            "- \"conversion failed\" → value string cannot be converted to property type\n" +
            "- \"value required\" → must provide value parameter",
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
                        description = "Name of the DependencyProperty to set (e.g., 'IsEnabled', 'Visibility', 'Text')"
                    },
                    value = new {
                        type = "string",
                        description = "String representation of value. Auto-converted to property type. Examples: 'True', '42', 'Red', 'Visible'"
                    }
                },
                required = new[] { "processId", "propertyName", "value" }
            },
            async (args, ct) => await new SetDpValueTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled", value = "False" },
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text", value = "New Value" }
            });

        RegisterTool(registry, "clear_dp_value",
            "[DependencyProperty] Clear a DependencyProperty local value, reverting it to its inherited, styled, or default value.\n\n" +
            "USE WHEN: Removing overrides applied by set_dp_value; testing default/inherited behavior.\n" +
            "DO NOT USE: On properties without local values (has no effect).\n\n" +
            "⚠️ WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  propertyName, clearedValue, newValue\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"property not found\" → verify propertyName is valid",
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
                        description = "Name of the DependencyProperty to clear (e.g., 'IsEnabled', 'Visibility')"
                    }
                },
                required = new[] { "processId", "propertyName" }
            },
            async (args, ct) => await new ClearDpValueTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" }
            });

        RegisterTool(registry, "watch_dp_changes",
            "[DependencyProperty] Register a listener for property value changes. NOTE: In STDIO transport, change events are NOT pushed. Use get_dp_value_source to poll for changes.\n\n" +
            "USE WHEN: HTTP+SSE transport is available (planned Phase 2+).\n" +
            "DO NOT USE: In STDIO mode - events cannot be pushed; use polling instead.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  watching: boolean,\n" +
            "  note: 'Events require HTTP+SSE transport'\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"property not found\" → verify propertyName is valid\n" +
            "- \"transport not supported\" → STDIO cannot push events",
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
                        description = "Name of the DependencyProperty to watch for changes (e.g., 'Text', 'IsEnabled')"
                    }
                },
                required = new[] { "processId", "propertyName" }
            },
            async (args, ct) => await new WatchDpChangesTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" },
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" }
            });
    }
}
