using System.Text.Json;
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
    private const string MvvmMetadata = "CATEGORY: MVVM | SAFETY: Check the SDK ReadOnly and Destructive flags before invoking this tool.\n\n";
    [McpServerTool(Name = "get_viewmodel", OpenWorld = false, ReadOnly = true)]
    [Description(
        MvvmMetadata + "[MVVM] Get the ViewModel (DataContext) of an element. Returns: typeName, " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
    public static Task<CallToolResult> GetViewModel(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional element ID whose DataContext should be inspected. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetViewModelTool>("GetViewModelTool", () => new GetViewModelTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_commands", OpenWorld = false, ReadOnly = true)]
    [Description(
        MvvmMetadata + "[MVVM] Get all ICommand properties from the ViewModel. Returns: commandName, " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> GetCommands(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional element ID whose ViewModel commands should be listed. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetCommandsTool>("GetCommandsTool", () => new GetCommandsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "execute_command", OpenWorld = false, Destructive = true)]
    [Description(
        MvvmMetadata + "[MVVM] Execute an ICommand on the ViewModel. Checks CanExecute first. " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345, commandName: \"SaveCommand\" }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", commandName: \"SaveCommand\" }")]
    public static Task<CallToolResult> ExecuteCommand(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("ICommand property name to execute, such as SaveCommand.")] string commandName,
        [Description("Optional element ID whose DataContext provides the command. Omit for the root window.")] string? elementId = null,
        [Description("Optional command parameter serialized as a string.")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("commandName", commandName),
            ("parameter", parameter));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ExecuteCommandTool>("ExecuteCommandTool", () => new ExecuteCommandTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_validation_errors", OpenWorld = false, ReadOnly = true)]
    [Description(
        MvvmMetadata + "[MVVM] Get validation errors from a WPF element. Returns IDataErrorInfo " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"AgeTextBox\" }")]
    public static Task<CallToolResult> GetValidationErrors(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional element ID whose validation errors should be returned. Omit to inspect the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetValidationErrorsTool>("GetValidationErrorsTool", () => new GetValidationErrorsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "modify_viewmodel", OpenWorld = false, Destructive = true)]
    [Description(
        MvvmMetadata + "[MVVM] Modify a ViewModel property value via reflection. UI updates automatically " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345, propertyName: \"Name\", value: \"John Doe\" }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Age\", value: 30 }")]
    public static Task<CallToolResult> ModifyViewModel(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("ViewModel property name to update at runtime.")] string propertyName,
        [Description("New property value encoded as raw JSON.")] JsonElement value,
        [Description("Optional element ID whose DataContext owns the property. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("value", value));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(
                "modify_viewmodel",
                () => new GenericPipeTool(sessionManager, "modify_viewmodel", GenericPipeTool.ExtractElementPropertyAndValueParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}



