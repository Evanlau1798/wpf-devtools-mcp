using System.Text.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Style/Template tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class StyleMcpTools
{
    [McpServerTool(Name = "get_applied_styles", Title = "Inspect WPF Applied Styles", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(StyleMcpToolDescriptions.GetAppliedStyles)]
    public static Task<CallToolResult> GetAppliedStyles(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose applied styles should be returned. Omit for the root window.")] string? elementId = null,
        [Description("Optional list of element IDs for batch inspection. Use either elementId or elementIds, not both.")] string[]? elementIds = null,
        [Description("Optional compact response mode. When true, return style summaries instead of full setter payloads.")] bool compact = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds),
            ("compact", compact));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetAppliedStylesTool>(sessionManager, "GetAppliedStylesTool", () => new GetAppliedStylesTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_triggers", Title = "Inspect WPF Triggers", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(StyleMcpToolDescriptions.GetTriggers)]
    public static Task<CallToolResult> GetTriggers(
        SessionManager sessionManager,
        [Description("Element ID whose style and template triggers should be listed.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetTriggersTool>(sessionManager, "GetTriggersTool", () => new GetTriggersTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_resource_chain", Title = "Trace WPF Resource Chain", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(StyleMcpToolDescriptions.GetResourceChain)]
    public static Task<CallToolResult> GetResourceChain(
        SessionManager sessionManager,
        [Description("XAML resource key to resolve, such as PrimaryBrush or ButtonStyle.")] string resourceKey,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional starting element ID for resource lookup. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("resourceKey", resourceKey));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetResourceChainTool>(sessionManager, "GetResourceChainTool", () => new GetResourceChainTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "override_style_setter", Title = "Override WPF Style Setter", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(StyleMcpToolDescriptions.OverrideStyleSetter)]
    public static Task<CallToolResult> OverrideStyleSetter(
        SessionManager sessionManager,
        [Description("Style-backed property name to override at runtime.")] string propertyName,
        [Description("New property value encoded as raw JSON.")] JsonElement value,
        [Description("Required element ID whose style setter should be overridden.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
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
            (a, ct) => ToolCallHelper.CachedTool<OverrideStyleSetterTool>(sessionManager, "OverrideStyleSetterTool", () => new OverrideStyleSetterTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "override_style_setter");
    }
}
