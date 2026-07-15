using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Tree and XAML tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class TreeMcpTools
{
    [McpServerTool(Name = "get_visual_tree", Title = "Inspect WPF Visual Tree", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(TreeMcpToolDescriptions.GetVisualTree)]
    public static Task<CallToolResult> GetVisualTree(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional starting element ID from get_visual_tree or get_logical_tree. Omit for the root window.")] string? elementId = null,
        [Range(0, TreeRequestOptions.MaxDepthLimit)]
        [Description("Optional maximum traversal depth. Use 2-4 for initial exploration.")] int? depth = null,
        [Description("Set true to omit null names and empty children arrays for smaller responses.")] bool compact = false,
        [Description("Set true to return a flat summary table instead of a nested tree for token-efficient browsing.")] bool summaryOnly = false,
        [Range(1, TreeRequestOptions.MaxNodesLimit)]
        [Description("Optional hard cap for the number of returned nodes. Defaults to 1000 when omitted.")] int? maxNodes = null,
        [Range(1, TreeRequestOptions.MaxChildrenPerNodeLimit)]
        [Description("Optional per-node child cap. Defaults to 200; extra children are reported in omittedChildCount.")] int? maxChildrenPerNode = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth),
            ("compact", compact),
            ("summaryOnly", summaryOnly),
            ("maxNodes", maxNodes),
            ("maxChildrenPerNode", maxChildrenPerNode));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetVisualTreeTool>(sessionManager, "GetVisualTreeTool", () => new GetVisualTreeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_logical_tree", Title = "Inspect WPF Logical Tree", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(TreeMcpToolDescriptions.GetLogicalTree)]
    public static Task<CallToolResult> GetLogicalTree(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional starting element ID from get_logical_tree or get_visual_tree. Omit for the root window.")] string? elementId = null,
        [Range(0, TreeRequestOptions.MaxDepthLimit)]
        [Description("Optional maximum traversal depth for the logical tree walk.")] int? depth = null,
        [Description("Set true to omit null names and empty children arrays for smaller responses.")] bool compact = false,
        [Description("Set true to return a flat summary table instead of a nested tree for token-efficient browsing.")] bool summaryOnly = false,
        [Range(1, TreeRequestOptions.MaxNodesLimit)]
        [Description("Optional hard cap for the number of returned nodes. Defaults to 1000 when omitted.")] int? maxNodes = null,
        [Range(1, TreeRequestOptions.MaxChildrenPerNodeLimit)]
        [Description("Optional per-node child cap. Defaults to 200; extra raw logical items are signaled as lower-bound omitted counts without fully enumerating large logical collections.")] int? maxChildrenPerNode = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth),
            ("compact", compact),
            ("summaryOnly", summaryOnly),
            ("maxNodes", maxNodes),
            ("maxChildrenPerNode", maxChildrenPerNode));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetLogicalTreeTool>(sessionManager, "GetLogicalTreeTool", () => new GetLogicalTreeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "serialize_to_xaml", Title = "Serialize WPF Element To Xaml", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(TreeMcpToolDescriptions.SerializeToXaml)]
    public static Task<CallToolResult> SerializeToXaml(
        SessionManager sessionManager,
        [Description("Current runtime element ID to serialize. Obtain this from get_ui_summary, find_elements, get_visual_tree, or get_logical_tree in the same session.")] string elementId,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, "serialize_to_xaml", () => new GenericPipeTool(sessionManager, "serialize_to_xaml")).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_namescope", Title = "Inspect WPF NameScope", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(TreeMcpToolDescriptions.GetNamescope)]
    public static Task<CallToolResult> GetNamescope(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional namescope root element ID. Omit for the root window.")] string? elementId = null,
        [Range(1, TreeTraversalDefaults.MaxNodesLimit)]
        [Description("Optional hard cap for the number of elements inspected while discovering names. Defaults to 10000.")] int? maxNodes = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("maxNodes", maxNodes));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, "get_namescope", () => new GenericPipeTool(sessionManager, "get_namescope", GenericPipeTool.ExtractNameScopeParams)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_template_tree", Title = "Inspect WPF Template Tree", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(TreeMcpToolDescriptions.GetTemplateTree)]
    public static Task<CallToolResult> GetTemplateTree(
        SessionManager sessionManager,
        [Description("Element ID of the templated control to inspect.")] string elementId,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional maximum traversal depth for the template visual tree.")] int? depth = null,
        [Range(1, TreeTraversalDefaults.MaxNodesLimit)]
        [Description("Optional hard cap for returned template tree nodes. Defaults to 1000.")] int? maxNodes = null,
        [Range(1, TreeTraversalDefaults.MaxChildrenPerNodeLimit)]
        [Description("Optional per-node child fan-out cap for template traversal. Defaults to 200.")] int? maxChildrenPerNode = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth),
            ("maxNodes", maxNodes),
            ("maxChildrenPerNode", maxChildrenPerNode));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetTemplateTreeTool>(sessionManager, "GetTemplateTreeTool", () => new GetTemplateTreeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_windows", Title = "List WPF Windows", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(TreeMcpToolDescriptions.GetWindows)]
    public static Task<CallToolResult> GetWindows(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, "get_windows", () => new GenericPipeTool(sessionManager, "get_windows")).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "find_elements", Title = "Find WPF Elements", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(TreeMcpToolDescriptions.FindElements)]
    public static Task<CallToolResult> FindElements(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional root element ID to search within. Omit for the root window.")] string? elementId = null,
        [Description("Optional general semantic query matched against element type, FrameworkElement.Name, AutomationId, Text, Content, or Header. Prefer precise filters for deterministic automation.")] string? query = null,
        [Description("Optional exact WPF type name filter, such as Button or TextBox.")] string? typeName = null,
        [Description("Compatibility alias for typeName. Prefer typeName for new calls; controlType is accepted for UI Automation-style clients.")] string? controlType = null,
        [Description("Optional WPF type name filters for OR-style matching. Use either typeName or typeNames, not both.")] string[]? typeNames = null,
        [Description("Optional exact FrameworkElement.Name filter.")] string? elementName = null,
        [Description("Optional exact AutomationProperties.AutomationId filter.")] string? automationId = null,
        [StringLength(256)]
        [Description("Optional exact property name filter, such as Text or Content. Maximum length: 256 characters.")] string? propertyName = null,
        [Description("Optional exact property value filter used with propertyName.")] string? propertyValue = null,
        [Description("Optional maximum number of results to return. Default: 20.")] int? maxResults = null,
        [Range(1, TreeTraversalDefaults.MaxNodesLimit)]
        [Description("Optional maximum number of elements to inspect before truncating traversal. Default: 1000; capped at 10000.")] int? maxTraversalNodes = null,
        [AllowedValues("exact", "contains")]
        [Description("Optional match mode: 'exact' (default) or 'contains' for case-insensitive substring matching.")] string? matchMode = null,
        [AllowedValues("exact", "assignable")]
        [Description("Optional WPF type matching mode: 'exact' (default) matches only the runtime type; 'assignable' also matches base types and implemented interfaces.")] string? typeMatchMode = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("query", query),
            ("typeName", typeName ?? controlType),
            ("typeNames", typeNames),
            ("elementName", elementName),
            ("automationId", automationId),
            ("propertyName", propertyName),
            ("propertyValue", propertyValue),
            ("maxResults", maxResults),
            ("maxTraversalNodes", maxTraversalNodes),
            ("matchMode", matchMode),
            ("typeMatchMode", typeMatchMode));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<FindElementsTool>(sessionManager, "FindElementsTool", () => new FindElementsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "compare_trees", Title = "Compare WPF Visual And Logical Trees", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(TreeMcpToolDescriptions.CompareTrees)]
    public static Task<CallToolResult> CompareTrees(
        SessionManager sessionManager,
        [Description(ToolDescriptionFragments.ActiveProcessIdParameter)] int? processId = null,
        [Description("Optional element ID to compare from instead of the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(sessionManager, "compare_trees", () => new GenericPipeTool(sessionManager, "compare_trees")).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
