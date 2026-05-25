using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Scene Diagnostics tools.
/// </summary>
[McpServerToolType]
public static class SceneDiagnosticsMcpTools
{

    [McpServerTool(Name = "get_state_diff", Title = "Inspect WPF Runtime State Diff", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(SceneDiagnosticsMcpToolDescriptions.GetStateDiff)]
    public static Task<CallToolResult> GetStateDiff(
        SessionManager sessionManager,
        [Description("Snapshot ID returned by capture_state_snapshot.")] string snapshotId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional human-readable description of the action that happened after the snapshot was captured.")] string? trigger = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("snapshotId", snapshotId),
            ("trigger", trigger));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetStateDiffTool>(sessionManager, 
                nameof(GetStateDiffTool),
                () => new GetStateDiffTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_state_diff");
    }

    [McpServerTool(Name = "get_element_snapshot", Title = "Inspect WPF Element Snapshot", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(SceneDiagnosticsMcpToolDescriptions.GetElementSnapshot)]
    public static Task<CallToolResult> GetElementSnapshot(
        SessionManager sessionManager,
        [Description("Runtime element ID to inspect.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional extra DependencyProperty names to append after the default snapshot property probes. Duplicates are ignored and defaults are always kept.")] string[]? includeProperties = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("includeProperties", includeProperties));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetElementSnapshotTool>(sessionManager, 
                nameof(GetElementSnapshotTool),
                () => new GetElementSnapshotTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_element_snapshot");
    }

    [McpServerTool(Name = "diagnose_visibility", Title = "Diagnose WPF Element Visibility", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(SceneDiagnosticsMcpToolDescriptions.DiagnoseVisibility)]
    public static Task<CallToolResult> DiagnoseVisibility(
        SessionManager sessionManager,
        [Description("Runtime element ID to inspect.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<DiagnoseVisibilityTool>(sessionManager, 
                nameof(DiagnoseVisibilityTool),
                () => new DiagnoseVisibilityTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "diagnose_visibility");
    }

    [McpServerTool(Name = "get_interaction_readiness", Title = "Inspect WPF Interaction Readiness", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(SceneDiagnosticsMcpToolDescriptions.GetInteractionReadiness)]
    public static Task<CallToolResult> GetInteractionReadiness(
        SessionManager sessionManager,
        [Description("Runtime element ID to inspect.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional interaction type label. Defaults to Click.")] string interactionType = "Click",
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("interactionType", interactionType));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetInteractionReadinessTool>(sessionManager, 
                nameof(GetInteractionReadinessTool),
                () => new GetInteractionReadinessTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_interaction_readiness");
    }

    [McpServerTool(Name = "get_ui_summary", Title = "Summarize WPF UI Semantics", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(SceneDiagnosticsMcpToolDescriptions.GetUiSummary)]
    public static Task<CallToolResult> GetUiSummary(
        SessionManager sessionManager,
        [Description("Optional runtime element ID to scope the semantic summary. Omit to summarize the root window.")] string? elementId = null,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Range(0, TreeRequestOptions.MaxDepthLimit)]
        [Description("Optional maximum visual depth to summarize. Omit to use the default semantic summary depth budget.")] int? depth = null,
        [AllowedValues("semantic", "visual")]
        [Description("Optional depth accounting mode: 'semantic' (default) skips layout-only wrapper levels when budgeting depth, while 'visual' counts every traversed level.")] string? depthMode = null,
        [Description("Set true to return only the semantic summary metadata and omit the nodes array for a lighter response.")] bool summaryOnly = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth),
            ("depthMode", depthMode),
            ("summaryOnly", summaryOnly));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetUiSummaryTool>(sessionManager, 
                nameof(GetUiSummaryTool),
                () => new GetUiSummaryTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_ui_summary");
    }

    [McpServerTool(Name = "get_form_summary", Title = "Summarize WPF Form State", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(SceneDiagnosticsMcpToolDescriptions.GetFormSummary)]
    public static Task<CallToolResult> GetFormSummary(
        SessionManager sessionManager,
        [Description("Optional runtime element ID to scope the form summary. Omit to use the root window.")] string? elementId = null,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Set true to keep framework-internal template controls such as RepeatButton or DataGrid header elements in the form summary. Default false keeps the response user-signal focused.")] bool includeFramework = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("includeFramework", includeFramework));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetFormSummaryTool>(sessionManager, 
                nameof(GetFormSummaryTool),
                () => new GetFormSummaryTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            toolName: "get_form_summary");
    }
}
