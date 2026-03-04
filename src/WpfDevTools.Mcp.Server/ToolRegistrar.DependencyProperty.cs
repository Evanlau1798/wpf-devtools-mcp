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
            "[DependencyProperty] Get the value source of a DependencyProperty. Returns where the current value comes from: Default, Inherited, Style, Trigger, TemplateBinding, LocalValue, or Animation. Essential for understanding why a property has a specific value.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to check (e.g., 'IsEnabled', 'Visibility', 'Text')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new GetDpValueSourceTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" },
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" }
            });

        RegisterTool(registry, "get_dp_metadata",
            "[DependencyProperty] Get DependencyProperty metadata including default value, inherits flag, affects measure/arrange, and coerce/validation callbacks. Useful for understanding property behavior and framework-level configuration. Returns: { defaultValue, inherits, affectsMeasure, affectsArrange, hasCoerceCallback }",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to get metadata for (e.g., 'IsEnabled', 'Visibility')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new GetDpMetadataTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, propertyName = "IsEnabled" },
                new { processId = 12345, propertyName = "Visibility" }
            });

        RegisterTool(registry, "set_dp_value",
            "[DependencyProperty] Set a DependencyProperty value at runtime. Value is a string that gets type-converted (e.g., 'True' for bool, 'Red' for Brush, 'Visible'/'Collapsed' for Visibility). Changes are not persisted.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to set (e.g., 'IsEnabled', 'Visibility', 'Text')" }, value = new { type = "string", description = "String representation of value. Auto-converted to property type. Examples: 'True', '42', 'Red', 'Visible'" } }, required = new[] { "processId", "propertyName", "value" } },
            async (args, ct) => await new SetDpValueTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled", value = "False" },
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text", value = "New Value" }
            });

        RegisterTool(registry, "clear_dp_value",
            "[DependencyProperty] Clear a DependencyProperty local value, reverting it to its inherited, styled, or default value. Useful for removing overrides applied by set_dp_value. Changes are not persisted.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to clear (e.g., 'IsEnabled', 'Visibility')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new ClearDpValueTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" }
            });

        RegisterTool(registry, "watch_dp_changes",
            "[DependencyProperty] Register a listener for property value changes. NOTE: In STDIO transport, change events are NOT pushed. Use get_dp_value_source to poll for changes. HTTP+SSE transport (planned) will support real-time events.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to watch for changes (e.g., 'Text', 'IsEnabled')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new WatchDpChangesTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" },
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" }
            });
    }
}
