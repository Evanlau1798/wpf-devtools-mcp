using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// MVVM tools registration (5 tools)
/// </summary>
public static partial class ToolRegistrar
{
    // === 9. MVVM (5 tools) ===
    private static void RegisterMvvmTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_viewmodel",
            "[MVVM] Get the ViewModel (DataContext) of an element. Returns: typeName, all properties with their current values, and whether INotifyPropertyChanged is implemented.\n\n" +
            "USE WHEN: Need to inspect ViewModel state; verify DataContext is set correctly.\n" +
            "DO NOT USE: For binding path issues (use get_datacontext_chain instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  typeName, implementsINotifyPropertyChanged: boolean,\n" +
            "  properties: [{ name, value, type }]\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"no datacontext\" → element has no DataContext set\n" +
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
            async (args, ct) => await new GetViewModelTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" }
            });

        RegisterTool(registry, "get_commands",
            "[MVVM] Get all ICommand properties from the ViewModel. Returns: commandName, canExecute status, commandType. Use to check why a button is disabled.\n\n" +
            "USE WHEN: Button is disabled; need to check ICommand.CanExecute status.\n" +
            "DO NOT USE: For non-MVVM apps (commands won't exist).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  commands: [{\n" +
            "    commandName, canExecute: boolean, commandType\n" +
            "  }]\n" +
            "}\n\n" +
            "Empty commands array means no ICommand properties found.\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"no datacontext\" → element has no ViewModel",
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
            async (args, ct) => await new GetCommandsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "execute_command",
            "[MVVM] Execute an ICommand on the ViewModel. Checks CanExecute first. Returns execution result.\n\n" +
            "USE WHEN: Testing command logic; simulating button clicks via command.\n" +
            "DO NOT USE: When CanExecute is false (will fail); check with get_commands first.\n\n" +
            "⚠️ WARNING: This triggers real application logic (saves data, navigates, etc.).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  executed: boolean,\n" +
            "  canExecute: boolean\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"command not found\" → verify commandName exists (use get_commands)\n" +
            "- \"cannot execute\" → CanExecute returned false\n" +
            "- \"commandName required\" → must specify which command",
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
                    commandName = new {
                        type = "string",
                        description = "Name of the ICommand property on the ViewModel (e.g., 'SaveCommand', 'DeleteCommand')"
                    },
                    parameter = new {
                        type = "string",
                        description = "Optional command parameter passed to ICommand.Execute(). String value.",
                        @default = (object?)null
                    }
                },
                required = new[] { "processId", "commandName" }
            },
            async (args, ct) => await new ExecuteCommandTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, commandName = "SaveCommand" },
                new { processId = 12345, elementId = "SaveButton", commandName = "SaveCommand" }
            });

        RegisterTool(registry, "get_validation_errors",
            "[MVVM] Get validation errors from a WPF element. Returns IDataErrorInfo and INotifyDataErrorInfo validation errors, plus Binding.ValidationRules failures.\n\n" +
            "USE WHEN: Form shows validation errors; need to understand validation state.\n" +
            "DO NOT USE: For binding path errors (use get_binding_errors instead).\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  errors: [{\n" +
            "    propertyName, errorMessage,\n" +
            "    errorType: 'IDataErrorInfo'|'INotifyDataErrorInfo'|'ValidationRule'\n" +
            "  }]\n" +
            "}\n\n" +
            "Empty errors array means no validation errors.\n\n" +
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
            async (args, ct) => await new GetValidationErrorsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "AgeTextBox" }
            });

        RegisterTool(registry, "modify_viewmodel",
            "[MVVM] Modify a ViewModel property value via reflection. UI updates automatically ONLY if the ViewModel implements INotifyPropertyChanged. Check get_viewmodel first to confirm property name.\n\n" +
            "USE WHEN: Testing UI updates with different ViewModel values; debugging binding issues.\n" +
            "DO NOT USE: For permanent changes (not persisted); when INotifyPropertyChanged is missing (UI won't update).\n\n" +
            "⚠️ WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
            "RESPONSE FORMAT:\n" +
            "{\n" +
            "  success: boolean,\n" +
            "  propertyName, oldValue, newValue\n" +
            "}\n\n" +
            "ERRORS:\n" +
            "- \"not connected\" → call connect(processId) first\n" +
            "- \"no datacontext\" → element has no ViewModel\n" +
            "- \"property not found\" → verify propertyName exists (use get_viewmodel)\n" +
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
                        description = "Name of the ViewModel property to modify (e.g., 'Name', 'Age', 'IsActive')"
                    },
                    value = new {
                        type = "string",
                        description = "New value as a string. Auto-converted to the property type. Examples: 'John Doe', '30', 'true'"
                    }
                },
                required = new[] { "processId", "propertyName", "value" }
            },
            async (args, ct) => await new GenericPipeTool(sessionManager, "modify_viewmodel",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var propertyName = ParameterParser.ParseStringParam(a, "propertyName");
                    var value = ParameterParser.ParseStringParam(a, "value");
                    if (string.IsNullOrEmpty(propertyName)) return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                    if (string.IsNullOrEmpty(value)) return (-1, null, (object)new { success = false, error = "Missing required parameter: value" });
                    return (pid, (object?)new { elementId = eid, propertyName, value }, null);
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, propertyName = "Name", value = "John Doe" },
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Age", value = "30" }
            });
    }
}
