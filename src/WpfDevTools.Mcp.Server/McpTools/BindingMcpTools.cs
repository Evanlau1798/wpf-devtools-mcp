using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Binding Diagnostics tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class BindingMcpTools
{

    [McpServerTool(Name = "get_binding_mismatches", Title = "Detect WPF Binding Mismatches", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(BindingMcpToolDescriptions.GetBindingMismatches)]
    public static Task<CallToolResult> GetBindingMismatches(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional element ID to inspect. Omit for the root window.")] string? elementId = null,
        [Description("When true, inspect descendant elements under the chosen root as well.")] bool recursive = false,
        [Description("When true, include unnamed framework template/internal mismatch entries that are excluded by default to reduce diagnostic noise.")] bool includeFramework = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("recursive", recursive),
            ("includeFramework", includeFramework));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetBindingMismatchesTool>(sessionManager, 
                "GetBindingMismatchesTool",
                () => new GetBindingMismatchesTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_binding_mismatches");
    }

    [McpServerTool(Name = "get_bindings", Title = "Inspect WPF Bindings", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(BindingMcpToolDescriptions.GetBindings)]
    public static Task<CallToolResult> GetBindings(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional element ID to inspect. Omit for the root window.")] string? elementId = null,
        [Description(ToolDescriptionFragments.BatchElementIdsParameter)] string[]? elementIds = null,
        [Description("When true, inspect descendant elements under the chosen root as well.")] bool? recursive = null,
        [AllowedValues("All", "Active", "Error")]
        [Description("Optional binding status filter such as 'All', 'Active', or 'Error'. Omit to return every binding.")] string? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds),
            ("recursive", recursive),
            ("statusFilter", statusFilter));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetBindingsTool>(sessionManager, "GetBindingsTool", () => new GetBindingsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_affected_elements", Title = "Find Best-Effort Affected Elements", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(BindingMcpToolDescriptions.GetAffectedElements)]
    public static Task<CallToolResult> GetAffectedElements(
        SessionManager sessionManager,
        [Description("ViewModel property name to match against simple binding paths, such as Name or IsEnabled.")] string propertyName,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional root element ID that scopes the search. Omit to scan from the root window.")] string? elementId = null,
        [Description("Optional coarse DataContext type filter. This narrows candidate elements but does not prove exact binding ownership.")] string? viewModelType = null,
        [Description("When true, scan descendants under the chosen root. Default true for subtree impact analysis.")] bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("viewModelType", viewModelType),
            ("recursive", recursive));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetAffectedElementsTool>(sessionManager, 
                nameof(GetAffectedElementsTool),
                () => new GetAffectedElementsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_affected_elements");
    }

    [McpServerTool(Name = "get_binding_errors", Title = "Diagnose WPF Binding Errors", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(BindingMcpToolDescriptions.GetBindingErrors)]
    public static Task<CallToolResult> GetBindingErrors(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID whose captured binding errors should be returned. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional maximum number of errors to return after filtering. Omit to return the full captured list.")] int? maxErrors = null,
        [Description("Optional ISO-8601 timestamp filter with explicit timezone, for example 2026-03-11T12:00:00Z or 2026-03-11T12:00:00+05:00. Only errors at or after this instant are returned.")] string? sinceTimestamp = null,
        [Description("When true, omit the verbose free-form message field and keep only the structured correlation fields for token-efficient triage. Defaults to true; set false when the full binding trace message is required.")] bool compact = true,
        [Description("Optional response contract control. Set navigation=false as an explicit opt-out when you want this call to omit both navigation and compatibility nextSteps.")] bool? navigation = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("maxErrors", maxErrors),
            ("sinceTimestamp", sinceTimestamp),
            ("compact", compact),
            ("navigation", navigation));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetBindingErrorsTool>(sessionManager, "GetBindingErrorsTool", () => new GetBindingErrorsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_binding_errors");
    }

    [McpServerTool(Name = "get_binding_value_chain", Title = "Trace WPF Binding Value Chain", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(BindingMcpToolDescriptions.GetBindingValueChain)]
    public static Task<CallToolResult> GetBindingValueChain(
        SessionManager sessionManager,
        [Description("DependencyProperty name whose binding value chain should be traced, such as Text.")] string propertyName,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional element ID that owns the bound property. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, 
                "get_binding_value_chain",
                () => new GenericPipeTool(sessionManager, "get_binding_value_chain", GenericPipeTool.ExtractElementAndPropertyParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_datacontext_chain", Title = "Trace WPF DataContext Chain", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(BindingMcpToolDescriptions.GetDataContextChain)]
    public static Task<CallToolResult> GetDataContextChain(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional element ID whose DataContext inheritance path should be returned.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetDataContextChainTool>(sessionManager, "GetDataContextChainTool", () => new GetDataContextChainTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "force_binding_update", Title = "Force WPF Binding Update", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(BindingMcpToolDescriptions.ForceBindingUpdate)]
    public static Task<CallToolResult> ForceBindingUpdate(
        SessionManager sessionManager,
        [Description("DependencyProperty name whose binding should be refreshed.")] string propertyName,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional element ID that owns the binding. Omit for the root window.")] string? elementId = null,
        [AllowedValues("Source", "Target")]
        [Description("Optional update direction: 'Source' (push UI value to ViewModel) or 'Target' (pull ViewModel value to UI). Default: Source.")] string? direction = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("direction", direction));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, 
                "force_binding_update",
                () => new GenericPipeTool(sessionManager, "force_binding_update", GenericPipeTool.ExtractElementAndPropertyParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "force_binding_update");
    }
}



