using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for MVVM tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class MvvmMcpTools
{
    private const string MvvmMetadata = "CATEGORY: MVVM\n\n";

    [McpServerTool(Name = "get_viewmodel", Title = "Inspect WPF ViewModel", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to inspect the current WPF ViewModel and runtime DataContext state for an element.\n\n" +
        MvvmMetadata + "[MVVM] Get the ViewModel (DataContext) of an element. Returns: typeName, " +
        "all properties with their current values, and whether INotifyPropertyChanged is implemented.\n\n" +
        "USE WHEN: Need to inspect ViewModel state; verify DataContext is set correctly.\n" +
        "DO NOT USE: For binding path issues (use get_datacontext_chain instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  typeName, implementsINotifyPropertyChanged: boolean,\n" +
        "  properties: [{ name, value, type, canWrite }]\n" +
        "}\n\n" +
        "FILTERING: Optional `propertyNames` lets agents request only the ViewModel properties relevant to the current diagnosis.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no datacontext\" -> element has no DataContext set\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }")]
    public static Task<CallToolResult> GetViewModel(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose DataContext should be inspected. Omit for the root window.")] string? elementId = null,
        [Description("Optional list of ViewModel property names to include. Omit to return all readable properties.")] string[]? propertyNames = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyNames", propertyNames));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetViewModelTool>(sessionManager, "GetViewModelTool", () => new GetViewModelTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_commands", Title = "Inspect WPF ViewModel Commands", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to inspect WPF ViewModel commands and understand runtime CanExecute state.\n\n" +
        MvvmMetadata + "[MVVM] Get all ICommand properties from the ViewModel. Returns: commandName, " +
        "canExecute status, commandType. Use to check why a button is disabled.\n\n" +
        "USE WHEN: Button is disabled; need to check ICommand.CanExecute status.\n" +
        "DO NOT USE: For non-MVVM apps (commands won't exist).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  commands: [{\n" +
        "    name, type, canExecute: boolean\n" +
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
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose ViewModel commands should be listed. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetCommandsTool>(sessionManager, "GetCommandsTool", () => new GetCommandsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "execute_command", Title = "Execute WPF ViewModel Command", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to execute a WPF ViewModel command without going through a button click path.\n\n" +
        MvvmMetadata + "[MVVM] Execute an ICommand on the ViewModel. Checks CanExecute first. " +
        "Returns execution result.\n\n" +
        "USE WHEN: Testing command logic; simulating button clicks via command.\n" +
        "DO NOT USE: When CanExecute is false (will fail); check with get_commands first.\n\n" +
        "WARNING: This triggers real application logic (saves data, navigates, etc.).\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep only the core command result, use `minimal` for the most concise success confirmation, or use `verbose` for requested/effective input + observedEffect; legacy `standard` remains accepted as a compatibility alias.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  commandName,\n" +
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
        [Description("ICommand property name to execute, such as SaveCommand.")] string commandName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose DataContext provides the command. Omit for the root window.")] string? elementId = null,
        [Description("Optional command parameter serialized as a string.")] string? parameter = null,
        [Description("Optional metadata detail mode: omit or use 'compact' (default), use 'minimal' for the most concise success confirmation, use 'verbose' for full additive metadata, or 'standard' as a compatibility alias.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("commandName", commandName),
            ("parameter", parameter),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ExecuteCommandTool>(sessionManager, "ExecuteCommandTool", () => new ExecuteCommandTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "execute_command");
    }

    [McpServerTool(Name = "get_validation_errors", Title = "Inspect WPF Validation Errors", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to inspect WPF validation errors across a runtime element subtree, including inactive tabs.\n\n" +
        MvvmMetadata + "[MVVM] Get validation errors from a WPF element and all its logical and visual descendants (recursive). " +
        "Returns all WPF validation errors (via Validation.GetErrors) aggregated from the target element and its entire subtree.\n\n" +
        "USE WHEN: Form shows validation errors; need to understand validation state; querying a parent to find all child validation errors at once.\n" +
        "BATCH MODE: Provide `elementIds` to inspect multiple scopes in one call. Single-target responses keep the original shape; batch responses return `results` with per-item `elementId` correlation.\n" +
        "DO NOT USE: For binding path errors (use get_binding_errors instead).\n\n" +
        "AGGREGATION: When called on a parent element (e.g., StackPanel, Grid, Window), " +
        "errors from ALL logical and visual descendant elements are collected recursively (max depth: 50, max errors: 200). " +
        "This includes inactive TabItem content and other subtree content that may not currently be visible in the visual tree. " +
        "Each error includes elementType and elementName to identify the source element.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  errorCount: number,\n" +
        "  errors: [{\n" +
        "    errorContent: string,\n" +
        "    isRuleError: boolean,\n" +
        "    ruleType: string,\n" +
        "    elementType: string,  // e.g., 'TextBox' - identifies which descendant has the error\n" +
        "    elementName: string|null  // x:Name of the element, if set\n" +
        "  }]\n" +
        "}\n\n" +
        "Empty errors array means no validation errors in the element or its subtree.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }  // get ALL validation errors in the app\n" +
        "- { processId: 12345, elementId: \"FormPanel\" }  // get errors in a specific form section\n" +
        "- { processId: 12345, elementId: \"AgeTextBox\" }  // get errors on a single element")]
    public static Task<CallToolResult> GetValidationErrors(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose validation errors should be returned. Omit to inspect the root window.")] string? elementId = null,
        [Description("Optional list of element IDs for batch inspection. Use either elementId or elementIds, not both.")] string[]? elementIds = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetValidationErrorsTool>(sessionManager, "GetValidationErrorsTool", () => new GetValidationErrorsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_validation_errors");
    }

    [McpServerTool(Name = "modify_viewmodel", Title = "Modify WPF ViewModel", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to modify a WPF ViewModel property during runtime debugging and UI verification.\n\n" +
        MvvmMetadata + "[MVVM] Modify a ViewModel property value via reflection. UI updates automatically " +
        "ONLY if the ViewModel implements INotifyPropertyChanged. Check get_viewmodel first to confirm property name.\n\n" +
        "USE WHEN: Testing UI updates with different ViewModel values; debugging binding issues.\n" +
        "DO NOT USE: For permanent changes (not persisted); when INotifyPropertyChanged is missing (UI won't update).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep the core mutation result, use `minimal` for success/property/newValue confirmation only, or use `verbose` for requested/effective input + observedEffect; legacy `standard` remains accepted as a compatibility alias.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, oldValue, newValue, propertyType, canWrite, requestedValueType, convertedValueType\n" +
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
        [Description("ViewModel property name to update at runtime.")] string propertyName,
        [Description("New property value encoded as raw JSON.")] JsonElement value,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose DataContext owns the property. Omit for the root window.")] string? elementId = null,
        [Description("Optional metadata detail mode: omit or use 'compact' (default), use 'minimal' for success/property/newValue confirmation only, use 'verbose' for full additive metadata, or 'standard' as a compatibility alias.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("value", value),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, 
                "modify_viewmodel",
                () => new GenericPipeTool(
                    sessionManager,
                    "modify_viewmodel",
                    GenericPipeTool.ExtractElementPropertyAndValueParams,
                    GenericPipeTool.AugmentModifyViewModelResult)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "modify_viewmodel");
    }
}
