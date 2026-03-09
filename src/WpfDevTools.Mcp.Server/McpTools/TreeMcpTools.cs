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
    private const string TreeMetadata = "CATEGORY: Tree | SAFETY: Check the SDK ReadOnly and Destructive flags before invoking this tool.\n\n";
    [McpServerTool(Name = "get_visual_tree", OpenWorld = false, ReadOnly = true)]
    [Description(
        TreeMetadata + "[Tree] Get the Visual Tree (rendering structure) of a WPF element. " +
        "Returns a hierarchical tree with elementId, type, name, and children for each node.\n\n" +
        "USE WHEN: You need to inspect template-generated elements, adorners, or the actual rendering structure.\n" +
        "DO NOT USE: Without depth parameter on large apps (use depth=2-4); use get_logical_tree for XAML structure only.\n\n" +
        "PERFORMANCE: Large trees (depth >5) can return 10,000+ elements. Always set depth=2-4 for initial exploration.\n" +
        "TOKEN EFFICIENCY: compact=true omits null/empty fields, summaryOnly=true returns a flat-summary-v1 table, maxNodes caps total returned nodes, maxChildrenPerNode caps fan-out per level.\n\n" +
        "RESPONSE FORMAT:\n" +
        "Nested mode:\n" +
        "{ success, tree, returnedNodeCount, omittedNodeCount, truncated, appliedOptions }\n" +
        "- tree: { elementId, type, name?, childCount, children?, omittedChildCount? }\n" +
        "Summary mode (summaryOnly=true):\n" +
        "{ success, format: \"flat-summary-v1\", columns, nodes, returnedNodeCount, omittedNodeCount, truncated, appliedOptions }\n" +
        "- columns: [elementId, type, name, childCount, depth, parentId]\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId from previous get_visual_tree call\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, depth: 3 }\n" +
        "- { processId: 12345, elementId: \"Button_1\", depth: 2 }")]
    public static Task<CallToolResult> GetVisualTree(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional starting element ID from get_visual_tree or get_logical_tree. Omit for the root window.")] string? elementId = null,
        [Description("Optional maximum traversal depth. Use 2-4 for initial exploration.")] int? depth = null,
        [Description("Set true to omit null names and empty children arrays for smaller responses.")] bool compact = false,
        [Description("Set true to return a flat summary table instead of a nested tree for token-efficient browsing.")] bool summaryOnly = false,
        [Description("Optional hard cap for the number of returned nodes. Use this to prevent oversized responses.")] int? maxNodes = null,
        [Description("Optional per-node child cap. Extra children are counted in omittedChildCount.")] int? maxChildrenPerNode = null,
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
            (a, ct) => ToolCallHelper.CachedTool<GetVisualTreeTool>("GetVisualTreeTool", () => new GetVisualTreeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_logical_tree", OpenWorld = false, ReadOnly = true)]
    [Description(
        TreeMetadata + "[Tree] Get the Logical Tree (semantic/XAML structure) of a WPF element. " +
        "Simpler than Visual Tree - shows only elements defined in XAML.\n\n" +
        "USE WHEN: You need to understand XAML structure, find named elements, or trace DataContext inheritance.\n" +
        "DO NOT USE: When you need to inspect template internals (use get_visual_tree or get_template_tree instead).\n\n" +
        "TOKEN EFFICIENCY: compact=true omits null/empty fields, summaryOnly=true returns a flat-summary-v1 table, maxNodes caps total returned nodes, maxChildrenPerNode caps fan-out per level.\n\n" +
        "RESPONSE FORMAT:\n" +
        "Nested mode:\n" +
        "{ success, tree, returnedNodeCount, omittedNodeCount, truncated, appliedOptions }\n" +
        "- tree: { elementId, type, name?, childCount, children?, omittedChildCount? }\n" +
        "Summary mode (summaryOnly=true):\n" +
        "{ success, format: \"flat-summary-v1\", columns, nodes, returnedNodeCount, omittedNodeCount, truncated, appliedOptions }\n" +
        "- columns: [elementId, type, name, childCount, depth, parentId]\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId is valid\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, depth: 5 }")]
    public static Task<CallToolResult> GetLogicalTree(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional starting element ID from get_logical_tree or get_visual_tree. Omit for the root window.")] string? elementId = null,
        [Description("Optional maximum traversal depth for the logical tree walk.")] int? depth = null,
        [Description("Set true to omit null names and empty children arrays for smaller responses.")] bool compact = false,
        [Description("Set true to return a flat summary table instead of a nested tree for token-efficient browsing.")] bool summaryOnly = false,
        [Description("Optional hard cap for the number of returned nodes. Use this to prevent oversized responses.")] int? maxNodes = null,
        [Description("Optional per-node child cap. Extra children are counted in omittedChildCount.")] int? maxChildrenPerNode = null,
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
            (a, ct) => ToolCallHelper.CachedTool<GetLogicalTreeTool>("GetLogicalTreeTool", () => new GetLogicalTreeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "serialize_to_xaml", OpenWorld = false, ReadOnly = true)]
    [Description(
        TreeMetadata + "[Tree] Serialize a WPF element to its XAML representation. " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> SerializeToXaml(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional element ID to serialize. Omit to serialize the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>("serialize_to_xaml", () => new GenericPipeTool(sessionManager, "serialize_to_xaml")).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_namescope", OpenWorld = false, ReadOnly = true)]
    [Description(
        TreeMetadata + "[Tree] Get the XAML NameScope of a WPF element. " +
        "Returns all named elements (x:Name) registered in the element's scope.\n\n" +
        "USE WHEN: You need to discover all named elements in a window or UserControl.\n" +
        "DO NOT USE: For finding elements by type (use get_visual_tree to browse the tree instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  namedElementCount: integer,\n" +
        "  namedElements: [{ name, elementId, type }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no namescope\" -> element is not a namescope root (try parent window)\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> GetNamescope(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional namescope root element ID. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>("get_namescope", () => new GenericPipeTool(sessionManager, "get_namescope")).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_template_tree", OpenWorld = false, ReadOnly = true)]
    [Description(
        TreeMetadata + "[Tree] Get the template Visual Tree of a templated WPF control (Button, ListBox, etc.). " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> GetTemplateTree(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Element ID of the templated control to inspect.")] string elementId,
        [Description("Optional maximum traversal depth for the template visual tree.")] int? depth = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("depth", depth));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetTemplateTreeTool>("GetTemplateTreeTool", () => new GetTemplateTreeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_windows", OpenWorld = false, ReadOnly = true)]
    [Description(
        TreeMetadata + "[Tree] Enumerate all open windows in the connected WPF application. " +
        "Returns each window's index, title, type, active state, and elementId.\n\n" +
        "USE WHEN: The target app has multiple windows and you need to inspect a secondary window " +
        "(e.g., dialogs, tool windows, child windows). Use the returned elementId as the elementId " +
        "parameter in get_visual_tree, get_logical_tree, and other tools to target that window.\n\n" +
        "DO NOT USE: For single-window apps where the default root is sufficient.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  windowCount: integer,\n" +
        "  windows: [{ index, title, type, isActive, elementId }]\n" +
        "}\n\n" +
        "WORKFLOW:\n" +
        "1. Call get_windows to discover all open windows\n" +
        "2. Use the elementId from the desired window as elementId in get_visual_tree, get_logical_tree, etc.\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> GetWindows(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>("get_windows", () => new GenericPipeTool(sessionManager, "get_windows")).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "compare_trees", OpenWorld = false, ReadOnly = true)]
    [Description(
        TreeMetadata + "[Tree] Compare Visual and Logical trees to identify structural differences. " +
        "Returns elements present in one tree but not the other.\n\n" +
        "USE WHEN: You need to understand which elements are template-generated vs XAML-defined.\n" +
        "DO NOT USE: On large apps without elementId scope (will be slow).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  visualChildCount: integer,\n" +
        "  logicalChildCount: integer,\n" +
        "  differenceCount: integer,\n" +
        "  differences: [{ type: \"VisualOnly\"|\"LogicalOnly\", elementType, elementId }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> CompareTrees(
        SessionManager sessionManager,
        [Description("Connected WPF process ID returned by get_processes.")] int processId,
        [Description("Optional element ID to compare from instead of the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>("compare_trees", () => new GenericPipeTool(sessionManager, "compare_trees")).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}



