using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Binding Diagnostics tools registration (5 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 3. Binding Diagnostics (5 tools) ===
    private static void RegisterBindingTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_bindings",
            "[Binding] Get all DataBindings on an element. Shows binding path, mode (OneWay/TwoWay/OneTime), source type, converter, and current status. Use recursive=true to include child elements.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, recursive = new { type = "boolean", description = "If true, include bindings from all child elements in the subtree. Default: false." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetBindingsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" },
                new { processId = 12345, recursive = true }
            });

        RegisterTool(registry, "get_binding_errors",
            "[Binding] Get ALL binding errors captured since Inspector connected. FIRST tool to use when debugging data display issues. Returns: elementType, elementName, propertyName, bindingPath, errorType, errorMessage for each error. Empty array means no errors.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new GetBindingErrorsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_binding_value_chain",
            "[Binding] Get the complete value resolution chain for a binding on a specific property. Shows each step from source to target including converters, fallback values, and StringFormat. Useful for diagnosing why a binding produces an unexpected value.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to inspect the binding chain for (e.g., 'Text', 'Content', 'IsEnabled')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "get_binding_value_chain",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var propertyName = ParameterParser.ParseStringParam(a, "propertyName");
                    if (string.IsNullOrEmpty(propertyName)) return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                    return (pid, (object?)new { elementId = eid, propertyName }, null);
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" }
            });

        RegisterTool(registry, "get_datacontext_chain",
            "[Binding] Get the DataContext inheritance chain from an element up to the root. Shows each ancestor's DataContext type and value. Essential for understanding why a binding can't find its source.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetDataContextChainTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "ErrorTextBox1" }
            });

        RegisterTool(registry, "force_binding_update",
            "[Binding] Force a binding to re-evaluate and transfer the current value. Use for UpdateSourceTrigger=Explicit bindings or when the source value changed but the UI didn't update. Triggers both UpdateSource and UpdateTarget.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty whose binding to force-update (e.g., 'Text', 'Content')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "force_binding_update",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var propertyName = ParameterParser.ParseStringParam(a, "propertyName");
                    if (string.IsNullOrEmpty(propertyName)) return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                    return (pid, (object?)new { elementId = eid, propertyName }, null);
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" }
            });
    }
}
