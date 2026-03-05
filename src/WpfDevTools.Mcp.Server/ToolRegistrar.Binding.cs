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
            "[Binding] Get all DataBindings on an element. Shows binding path, mode (OneWay/TwoWay/OneTime), source type, converter, and current status.\n\n" +
            "USE WHEN: You need to inspect binding configuration on a specific element or subtree.\n" +
            "DO NOT USE: recursive=true on large apps without elementId scope (will be slow).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  bindings: [{\n" +
            "    elementId, elementType, propertyName, bindingPath,\n" +
            "    mode: 'OneWay'|'TwoWay'|'OneTime'|'OneWayToSource',\n" +
            "    sourceType, converter, updateSourceTrigger, status\n" +
            "  }]\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"element not found\" → verify elementId from get_visual_tree",
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
                    recursive = new {
                        type = "boolean",
                        description = "If true, include bindings from all child elements in the subtree.",
                        @default = false
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new GetBindingsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" },
                new { processId = 12345, recursive = true }
            });

        RegisterTool(registry, "get_binding_errors",
            "[Binding] Get ALL binding errors captured since Inspector connected. FIRST tool to use when debugging data display issues.\n\n" +
            "USE WHEN: UI shows blank/wrong data, or you suspect binding path errors.\n" +
            "DO NOT USE: Before calling connect() - errors are only captured after injection.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  errors: [{\n" +
            "    elementType, elementName, propertyName, bindingPath,\n" +
            "    errorType: 'PathError'|'ConverterError'|'ValidationError',\n" +
            "    errorMessage\n" +
            "  }]\n" +
            "}\n\n" +
            "Empty errors array means no binding errors detected.\n\n" +
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
            async (args, ct) => await new GetBindingErrorsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_binding_value_chain",
            "[Binding] Get the complete value resolution chain for a binding on a specific property. Shows each step from source to target including converters, fallback values, and StringFormat.\n\n" +
            "USE WHEN: Binding doesn't error but shows unexpected value; need to trace value transformation.\n" +
            "DO NOT USE: Without propertyName - it's required.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  chain: [{\n" +
            "    step: 'Source'|'Converter'|'StringFormat'|'Fallback'|'Target',\n" +
            "    value, type\n" +
            "  }]\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"no binding\" → property has no binding (check with get_bindings first)\n" +
            "- \"propertyName required\" → must specify which property to inspect",
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
                        description = "Name of the DependencyProperty to inspect the binding chain for (e.g., 'Text', 'Content', 'IsEnabled')"
                    }
                },
                required = new[] { "processId", "propertyName" }
            },
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
            "[Binding] Get the DataContext inheritance chain from an element up to the root. Shows each ancestor's DataContext type and value.\n\n" +
            "USE WHEN: Binding path is correct but can't find source; need to understand DataContext inheritance.\n" +
            "DO NOT USE: When binding error already shows the issue (use get_binding_errors first).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  chain: [{\n" +
            "    elementId, elementType, dataContextType, dataContextValue, isInherited\n" +
            "  }]\n" +
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
                    }
                },
                required = new[] { "processId" }
            },
            async (args, ct) => await new GetDataContextChainTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "ErrorTextBox1" }
            });

        RegisterTool(registry, "force_binding_update",
            "[Binding] Force a binding to re-evaluate and transfer the current value. Use for UpdateSourceTrigger=Explicit bindings or when the source value changed but the UI didn't update.\n\n" +
            "USE WHEN: UI is stale despite source changes; testing UpdateSourceTrigger=Explicit bindings.\n" +
            "DO NOT USE: As a workaround for broken INotifyPropertyChanged (fix the ViewModel instead).\n\n" +
            "⚠️ WARNING: This modifies the running app (triggers UpdateSource and UpdateTarget).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  updated: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"no binding\" → property has no binding\n" +
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
                        description = "Name of the DependencyProperty whose binding to force-update (e.g., 'Text', 'Content')"
                    }
                },
                required = new[] { "processId", "propertyName" }
            },
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
