using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Tree and XAML tools (6 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class TreeMcpTools
{
    [McpServerTool(Name = "get_visual_tree", ReadOnly = true)]
    [Description(
        "[Tree] Get the Visual Tree (rendering structure) of a WPF element. " +
        "Returns a hierarchical tree with elementId, type, name, and children for each node.\n\n" +
        "USE WHEN: You need to inspect template-generated elements, adorners, or the actual rendering structure.\n" +
        "DO NOT USE: Without depth parameter on large apps (use depth=2-4); use get_logical_tree for XAML structure only.\n\n" +
        "PERFORMANCE: Large trees (depth >5) can return 10,000+ elements. Always set depth=2-4 for initial exploration.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  tree: { elementId, type, name, childCount, children: [...] },\n" +
        "  totalElements: integer\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId from previous get_visual_tree call\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, depth: 3 }\n" +
        "- { processId: 12345, elementId: \"Button_1\", depth: 2 }")]
    public static Task<CallToolResult> GetVisualTree(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        int? depth = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetVisualTreeTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_logical_tree", ReadOnly = true)]
    [Description(
        "[Tree] Get the Logical Tree (semantic/XAML structure) of a WPF element. " +
        "Simpler than Visual Tree - shows only elements defined in XAML.\n\n" +
        "USE WHEN: You need to understand XAML structure, find named elements, or trace DataContext inheritance.\n" +
        "DO NOT USE: When you need to inspect template internals (use get_visual_tree or get_template_tree instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  tree: { elementId, type, name, childCount, children: [...] }\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId is valid\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, depth: 5 }")]
    public static Task<CallToolResult> GetLogicalTree(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        int? depth = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetLogicalTreeTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "serialize_to_xaml", ReadOnly = true)]
    [Description(
        "[Tree] Serialize a WPF element to its XAML representation. " +
        "Returns the XAML markup string for the element and its children.\n\n" +
        "USE WHEN: You need to understand element structure in markup form or export UI definition.\n" +
        "DO NOT USE: On large subtrees (use elementId to scope to specific element).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  xaml: string\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"serialization failed\" -> element may contain non-serializable properties\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> SerializeToXaml(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GenericPipeTool(sessionManager, "serialize_to_xaml").ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_namescope", ReadOnly = true)]
    [Description(
        "[Tree] Get the XAML NameScope of a WPF element. " +
        "Returns all named elements (x:Name) registered in the element's scope.\n\n" +
        "USE WHEN: You need to discover all named elements in a window or UserControl.\n" +
        "DO NOT USE: For finding elements by type (use get_visual_tree with filter instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  names: [{ name, elementId, type }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no namescope\" -> element is not a namescope root (try parent window)\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> GetNamescope(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GenericPipeTool(sessionManager, "get_namescope").ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_template_tree", ReadOnly = true)]
    [Description(
        "[Tree] Get the template Visual Tree of a templated WPF control (Button, ListBox, etc.). " +
        "Shows the internal rendering structure defined by the control's ControlTemplate.\n\n" +
        "USE WHEN: You need to inspect how a control renders internally or find template parts.\n" +
        "DO NOT USE: On non-templated elements (will return empty); check element type first.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  tree: { elementId, type, name, childCount, children: [...] }\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no template\" -> element is not a templated control\n" +
        "- \"elementId required\" -> must specify which control to inspect\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> GetTemplateTree(
        SessionManager sessionManager,
        int processId,
        string elementId,
        int? depth = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetTemplateTreeTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "compare_trees", ReadOnly = true)]
    [Description(
        "[Tree] Compare Visual and Logical trees to identify structural differences. " +
        "Returns elements present in one tree but not the other.\n\n" +
        "USE WHEN: You need to understand which elements are template-generated vs XAML-defined.\n" +
        "DO NOT USE: On large apps without elementId scope (will be slow).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  onlyInVisual: [{ elementId, type }],\n" +
        "  onlyInLogical: [{ elementId, type }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> CompareTrees(
        SessionManager sessionManager,
        int processId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GenericPipeTool(sessionManager, "compare_trees").ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
