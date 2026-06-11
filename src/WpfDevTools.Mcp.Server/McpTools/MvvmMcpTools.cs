using System.Text.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

    [McpServerTool(Name = "get_viewmodel", Title = "Inspect WPF ViewModel", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(MvvmMcpToolDescriptions.GetViewModel)]
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
    [Description(MvvmMcpToolDescriptions.GetCommands)]
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
    [Description(MvvmMcpToolDescriptions.ExecuteCommand)]
    public static Task<CallToolResult> ExecuteCommand(
        SessionManager sessionManager,
        [Description("ICommand property name to execute, such as SaveCommand.")] string commandName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose DataContext provides the command. Omit for the root window.")] string? elementId = null,
        [Description("Optional command parameter serialized as a string.")] string? parameter = null,
        [AllowedValues("compact", "minimal", "verbose", "standard")]
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
    [Description(MvvmMcpToolDescriptions.GetValidationErrors)]
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
    [Description(MvvmMcpToolDescriptions.ModifyViewModel)]
    public static Task<CallToolResult> ModifyViewModel(
        SessionManager sessionManager,
        [Description("ViewModel property name to update at runtime.")] string propertyName,
        [Description("New property value encoded as raw JSON.")] JsonElement value,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose DataContext owns the property. Omit for the root window.")] string? elementId = null,
        [AllowedValues("compact", "minimal", "verbose", "standard")]
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
