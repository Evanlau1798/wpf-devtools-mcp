namespace WpfDevTools.Mcp.Server.McpTools;

internal static class TreeMcpToolDescriptions
{
    private const string TreeMetadata = "CATEGORY: Tree\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetVisualTree =
        "Use this tool to inspect the runtime visual tree of a running WPF window or element.\n\n" +
        TreeMetadata + "[Tree] Get the Visual Tree (rendering structure) of a WPF element. " +
        "Returns a hierarchical tree with elementId, type, name, and children for each node.\n\n" +
        "USE WHEN: You need to inspect template-generated elements, adorners, or the actual rendering structure.\n" +
        "DO NOT USE: Without depth parameter on large apps (use depth=2-4); use get_logical_tree for XAML structure only.\n\n" +
        "PERFORMANCE: Large trees (depth >5) can return 10,000+ elements. Always set depth=2-4 for initial exploration.\n" +
        "TOKEN EFFICIENCY: compact=true omits null/empty fields, summaryOnly=true returns a flat-summary-v1 table. " +
        "maxNodes caps total returned nodes (default 1000), and maxChildrenPerNode caps fan-out per level (default 200).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "- Nested mode:\n" +
        "- success, tree, depthSufficiencyHint (optional), returnedNodeCount, omittedNodeCount, truncated, appliedOptions\n" +
        "- tree: { elementId, type, name (optional), childCount, children (optional), omittedChildCount (optional) }\n" +
        "- Summary mode (summaryOnly=true):\n" +
        "- success, format: \"flat-summary-v1\", columns, nodes, depthSufficiencyHint (optional), returnedNodeCount, omittedNodeCount, truncated, appliedOptions\n" +
        "- columns: [elementId, type, name, childCount, depth, parentId]\n\n" +
        "- depthSufficiencyHint: { isSufficient, reasonCode, currentDepth, recommendedDepth, suggestion } when deeper traversal is likely required\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId from previous get_visual_tree call\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"depth\": 3 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"Button_1\", \"depth\": 2 }";

    public const string GetLogicalTree =
        "Use this tool to inspect the WPF logical tree when you need runtime XAML structure rather than render-only details.\n\n" +
        TreeMetadata + "[Tree] Get the Logical Tree (semantic/XAML structure) of a WPF element. " +
        "Simpler than Visual Tree - shows only elements defined in XAML.\n\n" +
        "USE WHEN: You need to understand XAML structure, find named elements, or trace DataContext inheritance.\n" +
        "DO NOT USE: When you need to inspect template internals (use get_visual_tree or get_template_tree instead).\n\n" +
        "TOKEN EFFICIENCY: compact=true omits null/empty fields, summaryOnly=true returns a flat-summary-v1 table. " +
        "maxNodes caps total returned nodes (default 1000), and maxChildrenPerNode caps fan-out per level (default 200).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "- Nested mode:\n" +
        "- success, tree, depthSufficiencyHint (optional), returnedNodeCount, omittedNodeCount, truncated, appliedOptions\n" +
        "- tree: { elementId, type, name (optional), childCount, childCountExact (optional), hasMoreChildren (optional), children (optional), omittedChildCount (optional) }\n" +
        "- Summary mode (summaryOnly=true):\n" +
        "- success, format: \"flat-summary-v1\", columns, nodes, depthSufficiencyHint (optional), returnedNodeCount, omittedNodeCount, truncated, appliedOptions\n" +
        "- columns: [elementId, type, name, childCount, depth, parentId, childCountExact, hasMoreChildren]\n" +
        "- For capped logical collections, childCount, omittedChildCount, and omittedNodeCount are lower-bound sentinel counts when childCountExact=false / hasMoreChildren=true.\n" +
        "- hasMoreChildren can mean uninspected raw logical items remain, not necessarily DependencyObject nodes.\n\n" +
        "- depthSufficiencyHint: { isSufficient, reasonCode, currentDepth, recommendedDepth, suggestion } when deeper traversal is likely required\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId is valid\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"depth\": 5 }";

    public const string SerializeToXaml =
        "Use this tool to serialize a live WPF element into a safe runtime XAML snapshot for inspection and comparison.\n\n" +
        TreeMetadata + "[Tree] Serialize a WPF element to a bounded XAML-like runtime snapshot. " +
        "Returns a markup string for the element and a capped set of children without invoking WPF XamlWriter round-trip serialization.\n\n" +
        "USE WHEN: You need to understand element structure in markup form after narrowing to a concrete live elementId.\n" +
        "DO NOT USE: As a design-time XAML export, source-code round trip, root-window dump, or with selector-style arguments. Window/root elementIds are rejected before serialization. First use get_ui_summary, get_visual_tree, get_logical_tree, or find_elements to obtain a smaller current elementId.\n\n" +
        "REQUEST FORMAT:\n" +
        "- elementId: required current runtime element ID from this connected session\n" +
        "- processId: optional after connect(processId) or select_active_process(processId)\n" +
        "- Unsupported: selector, maxDepth, and maxNodes are rejected before pipe access; use tree tools to narrow scope first.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean\n" +
        "  - xaml: safe runtime snapshot string when success=true\n" +
        "  - errorCode: InvalidArgument with reasonCode=RootWindowSerializationBlocked when elementId targets a root Window scope\n" +
        "  - errorCode: XamlSerializationFailed when the safe snapshot writer cannot inspect the requested runtime element\n" +
        "  - errorData: { elementId, elementType, reasonCode } for blocked root/window scopes\n" +
        "  - errorData: { elementId, elementType, exceptionType } for serialization failures\n\n" +
        "ERRORS:\n" +
        "- \"missing elementId\" -> call get_ui_summary, get_visual_tree, get_logical_tree, or find_elements first\n" +
        "- \"unknown argument\" -> use only processId and elementId\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"InvalidArgument\" with reasonCode=RootWindowSerializationBlocked -> target a smaller descendant elementId first\n" +
        "- \"XamlSerializationFailed\" -> the runtime snapshot writer could not inspect this element; inspect a smaller subtree first\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }";

    public const string GetNamescope =
        "Use this tool to inspect a WPF namescope and discover runtime named elements, including inactive tabs.\n\n" +
        TreeMetadata + "[Tree] Get the XAML NameScope of a WPF element. " +
        "Returns all named elements (x:Name) registered in the element's scope.\n\n" +
        "USE WHEN: You need to discover all named elements in a window or UserControl, including names registered for inactive tabs or other logical-only content.\n" +
        "DO NOT USE: For finding elements by type (use get_visual_tree to browse the tree instead).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - hasNameScope: boolean,\n" +
        "  - namedElementCount: integer,\n" +
        "  - traversalNodeCount: integer,\n" +
        "  - maxTraversalNodes: integer,\n" +
        "  - traversalTruncated: boolean,\n" +
        "  - namedElements: [{ name, elementId, type }]\n\n" +
        "NO NAMESCOPE: If the element is not a namescope root, the response is success=true with hasNameScope=false, namedElementCount=0, traversalNodeCount=0, and traversalTruncated=false. Try the parent window or UserControl when names are expected.\n" +
        "TRUNCATION: traversalTruncated=true only means a namescope root was present and maxNodes stopped name discovery before all descendants were inspected. Omitted maxNodes defaults to 10000 for namescope discovery.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }";

    public const string GetTemplateTree =
        "Use this tool to inspect the WPF template tree of a runtime control and understand generated parts.\n\n" +
        TreeMetadata + "[Tree] Get the template Visual Tree of a templated WPF control (Button, ListBox, etc.). " +
        "Shows the internal rendering structure defined by the control's ControlTemplate.\n\n" +
        "USE WHEN: You need to inspect how a loaded templated control renders internally or find template parts.\n" +
        "TARGET SELECTION: Pick a loaded templated control from the current visual tree, such as a Button, ListBox, ComboBox, or TabControl. If ElementNotLoaded or \"No template visual tree found\" is returned for one candidate, choose another current loaded templated control before reporting a template-tree limitation.\n" +
        "DO NOT USE: On non-templated elements (will return empty); check element type first.\n\n" +
        "PERFORMANCE: Template traversal applies the same default 1000-node and 200-child fan-out caps as visual/logical tree tools; pass maxNodes or maxChildrenPerNode to narrow large template payloads.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - tree: { elementId, type, name, childCount, children: [...], omittedChildCount (optional) },\n" +
        "  - returnedNodeCount: number,\n" +
        "  - omittedNodeCount: number,\n" +
        "  - truncated: boolean\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no template\" -> element is not a templated control\n" +
        "- \"elementId required\" -> must specify which control to inspect\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }";

    public const string GetWindows =
        "Use this tool to inspect secondary windows and list WPF windows before targeting a specific runtime root.\n\n" +
        TreeMetadata + "[Tree] Enumerate all open windows in the connected WPF application. " +
        "Returns each window's index, title, type, focus snapshot, and elementId using camelCase field names.\n\n" +
        "USE WHEN: The target app has multiple windows and you need to inspect a secondary window " +
        "(e.g., dialogs, tool windows, child windows). Use the returned elementId as the elementId " +
        "parameter in get_visual_tree, get_logical_tree, and other tools to target that window.\n\n" +
        "DO NOT USE: For single-window apps where the default root is sufficient.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - windowCount: integer,\n" +
        "  - windows: [{ index, title, type, isActive, isVisible, isMainWindow, elementId }]\n\n" +
        "NOTE: isActive is a point-in-time focus snapshot and may change between calls; use isVisible/isMainWindow to interpret transient focus timing.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"pipe disconnected\" -> target app may have closed; reconnect\n\n" +
        "WORKFLOW:\n" +
        "1. Call get_windows to discover all open windows\n" +
        "2. Use the elementId from the desired window as elementId in get_visual_tree, get_logical_tree, etc.\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }";

    public const string FindElements =
        "Use this tool to search a running WPF tree for matching runtime elements without expanding the full visual tree first.\n\n" +
        TreeMetadata + "[Tree] Search visual and logical descendants from the chosen root using exact or contains filters. " +
        "Results are bounded by maxResults and traversal is bounded by maxTraversalNodes to protect the target UI thread.\n\n" +
        "USE WHEN: You need a compact lookup entry point before calling get_visual_tree, get_layout_info, get_bindings, or other element-scoped tools.\n" +
        "DO NOT USE: As a full query language. The query parameter is a bounded convenience lookup over common semantic fields; use precise filters when automation needs determinism.\n\n" +
        "SUPPORTED FILTERS:\n" +
        "- query: bounded semantic query across element type, FrameworkElement.Name, AutomationId, Text, Content, and Header; defaults to contains matching when matchMode is omitted\n" +
        "- typeName\n" +
        "- controlType (compatibility alias for typeName; prefer typeName in new calls)\n" +
        "- typeNames\n" +
        "- elementName\n" +
        "- automationId\n" +
        "- propertyName + propertyValue\n" +
        "- matchMode: exact | contains\n" +
        "- typeMatchMode: exact | assignable; assignable includes base types and implemented interfaces\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - resultCount: integer,\n" +
        "  - truncated: boolean,\n" +
        "  - traversalNodeCount: integer,\n" +
        "  - maxTraversalNodes: integer,\n" +
          "  - traversalTruncated: boolean,\n" +
          "  - truncationReason (optional): 'maxResults'|'maxTraversalNodes',\n" +
          "  - searchComplete: boolean; false means maxResults or traversal bounds left the result set incomplete,\n" +
          "  - recovery (optional): structured retry budget and narrower-root guidance for a traversal-truncated zero-result search,\n" +
        "  - results: [{ elementId, elementType, elementName, automationId, matchedProperty, matchedValue }]\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify the root elementId before retrying\n" +
        "- \"maxResults\" -> must be a positive integer\n" +
        "- \"maxTraversalNodes\" -> must be a positive integer\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"query\": \"Apply\", \"maxResults\": 10 }\n" +
        "- { \"processId\": 12345, \"typeName\": \"Button\", \"maxResults\": 10 }\n" +
        "- { \"processId\": 12345, \"typeName\": \"ButtonBase\", \"typeMatchMode\": \"assignable\" }\n" +
        "- { \"processId\": 12345, \"controlType\": \"Button\", \"maxResults\": 10 }\n" +
        "- { \"processId\": 12345, \"elementName\": \"SaveButton\" }\n" +
        "- { \"processId\": 12345, \"propertyName\": \"Text\", \"propertyValue\": \"Ready\" }";

    public const string CompareTrees =
        "Use this tool to compare WPF visual and logical trees when runtime structure does not match XAML structure.\n\n" +
        TreeMetadata + "[Tree] Compare Visual and Logical trees to identify structural differences. " +
        "Returns elements present in one tree but not the other.\n\n" +
        "USE WHEN: You need to understand which elements are template-generated vs XAML-defined.\n" +
        "DO NOT USE: On large apps without elementId scope (will be slow).\n\n" +
        "PERFORMANCE: Scope with elementId for apps with >1000 elements. Full-tree comparison may exceed the tool timeout on complex UIs.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - visualChildCount: integer,\n" +
        "  - logicalChildCount: integer,\n" +
        "  - differenceCount: integer,\n" +
        "  - differences: [{ type: \"VisualOnly\"|\"LogicalOnly\", elementType, elementId }]\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }";
}
