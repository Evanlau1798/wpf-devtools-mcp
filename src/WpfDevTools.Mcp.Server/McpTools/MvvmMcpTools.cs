using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for MVVM tools (5 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class MvvmMcpTools
{
    [McpServerTool(Name = "get_viewmodel", ReadOnly = true)]
    [Description(
        "[MVVM] Get the ViewModel (DataContext) of an element. Returns: typeName, " +
        "all properties with their current values, and whether INotifyPropertyChanged is implemented.\n\n" +
        "USE WHEN: Need to inspect ViewModel state; verify DataContext is set correctly.\n" +
        "DO NOT USE: For binding path issues (use get_datacontext_chain instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  typeName, implementsINotifyPropertyChanged: boolean,\n" +
        "  properties: [{ name, value, type }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no datacontext\" -> element has no DataContext set\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
    public static Task<CallToolResult> GetViewModel(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetViewModelTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_commands", ReadOnly = true)]
    [Description(
        "[MVVM] Get all ICommand properties from the ViewModel. Returns: commandName, " +
        "canExecute status, commandType. Use to check why a button is disabled.\n\n" +
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
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no datacontext\" -> element has no ViewModel\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> GetCommands(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetCommandsTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "execute_command", Destructive = true)]
    [Description(
        "[MVVM] Execute an ICommand on the ViewModel. Checks CanExecute first. " +
        "Returns execution result.\n\n" +
        "USE WHEN: Testing command logic; simulating button clicks via command.\n" +
        "DO NOT USE: When CanExecute is false (will fail); check with get_commands first.\n\n" +
        "WARNING: This triggers real application logic (saves data, navigates, etc.).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  executed: boolean,\n" +
        "  canExecute: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"command not found\" -> verify commandName exists (use get_commands)\n" +
        "- \"cannot execute\" -> CanExecute returned false\n" +
        "- \"commandName required\" -> must specify which command\n\n" +
        "Examples:\n" +
        "- { processId: 12345, commandName: \"SaveCommand\" }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", commandName: \"SaveCommand\" }")]
    public static Task<CallToolResult> ExecuteCommand(
        SessionManager sessionManager,
        int processId,
        string commandName,
        string? elementId = null,
        string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("commandName", commandName),
            ("parameter", parameter));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new ExecuteCommandTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_validation_errors", ReadOnly = true)]
    [Description(
        "[MVVM] Get validation errors from a WPF element. Returns IDataErrorInfo " +
        "and INotifyDataErrorInfo validation errors, plus Binding.ValidationRules failures.\n\n" +
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
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"AgeTextBox\" }")]
    public static Task<CallToolResult> GetValidationErrors(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetValidationErrorsTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "modify_viewmodel", Destructive = true)]
    [Description(
        "[MVVM] Modify a ViewModel property value via reflection. UI updates automatically " +
        "ONLY if the ViewModel implements INotifyPropertyChanged. Check get_viewmodel first to confirm property name.\n\n" +
        "USE WHEN: Testing UI updates with different ViewModel values; debugging binding issues.\n" +
        "DO NOT USE: For permanent changes (not persisted); when INotifyPropertyChanged is missing (UI won't update).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, oldValue, newValue\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no datacontext\" -> element has no ViewModel\n" +
        "- \"property not found\" -> verify propertyName exists (use get_viewmodel)\n" +
        "- \"conversion failed\" -> value cannot be converted to property type\n" +
        "- \"propertyName required\" -> must specify which property\n" +
        "- \"value required\" -> must provide new value\n\n" +
        "Examples:\n" +
        "- { processId: 12345, propertyName: \"Name\", value: \"John Doe\" }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Age\", value: \"30\" }")]
    public static Task<CallToolResult> ModifyViewModel(
        SessionManager sessionManager,
        int processId,
        string propertyName,
        string value,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("value", value));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GenericPipeTool(sessionManager, "modify_viewmodel",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var prop = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(a, "propertyName");
                    var val = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(a, "value");
                    if (string.IsNullOrEmpty(prop))
                        return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                    if (string.IsNullOrEmpty(val))
                        return (-1, null, (object)new { success = false, error = "Missing required parameter: value" });
                    return (pid, (object?)new { elementId = eid, propertyName = prop, value = val }, null);
                }).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
