using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Mcp.Server.McpResources;

internal static class ResponseContractParameterConstraints
{
    public static object[] GetParameterConstraints()
    {
        return new object[]
        {
            Integer("wait_for_dp_change", "timeoutMs", defaultValue: 5000, minimum: 1, maximum: 30000),
            Integer("wait_for_dp_change", "pollIntervalMs", defaultValue: 200, minimum: 50, maximum: 5000),
            Integer("wait_for_dp_change_after_mutation", "timeoutMs", defaultValue: 5000, minimum: 1, maximum: 30000),
            Integer("wait_for_dp_change_after_mutation", "pollIntervalMs", defaultValue: 200, minimum: 50, maximum: 5000),
            StringEnum("get_processes", "windowFilter", defaultValue: "visible", "visible", "all", "foreground"),
            Integer("connect", "processId", defaultValue: null, minimum: 1, maximum: int.MaxValue),
            StringEnum("connect", "selectionStrategy", defaultValue: "single_only", "single_only", "largest_working_set"),
            StringEnum("connect", "windowFilter", defaultValue: "visible", "visible", "all", "foreground"),
            Integer("get_visual_tree", "depth", defaultValue: null, minimum: 0, maximum: TreeRequestOptions.MaxDepthLimit),
            Integer("get_visual_tree", "maxNodes", defaultValue: TreeTraversalDefaults.DefaultMaxNodes, minimum: 1, maximum: TreeRequestOptions.MaxNodesLimit),
            Integer("get_visual_tree", "maxChildrenPerNode", defaultValue: TreeTraversalDefaults.DefaultMaxChildrenPerNode, minimum: 1, maximum: TreeRequestOptions.MaxChildrenPerNodeLimit),
            Integer("get_logical_tree", "depth", defaultValue: null, minimum: 0, maximum: TreeRequestOptions.MaxDepthLimit),
            Integer("get_logical_tree", "maxNodes", defaultValue: TreeTraversalDefaults.DefaultMaxNodes, minimum: 1, maximum: TreeRequestOptions.MaxNodesLimit),
            Integer("get_logical_tree", "maxChildrenPerNode", defaultValue: TreeTraversalDefaults.DefaultMaxChildrenPerNode, minimum: 1, maximum: TreeRequestOptions.MaxChildrenPerNodeLimit),
            Integer("find_elements", "maxTraversalNodes", defaultValue: TreeTraversalDefaults.DefaultMaxNodes, minimum: 1, maximum: TreeTraversalDefaults.MaxNodesLimit),
            Integer("get_namescope", "maxNodes", defaultValue: TreeTraversalDefaults.DefaultNameScopeMaxNodes, minimum: 1, maximum: TreeTraversalDefaults.MaxNodesLimit),
            Integer("trace_routed_events", "durationMs", defaultValue: 5000, minimum: 0, maximum: TraceRoutedEventsTool.MaxDurationMs),
            Integer("trace_routed_events", "maxEvents", defaultValue: null, minimum: 1, maximum: null),
            Integer("drain_events", "maxEvents", defaultValue: null, minimum: 1, maximum: null),
            Integer("get_binding_errors", "maxErrors", defaultValue: null, minimum: 1, maximum: null),
            Integer("get_ui_summary", "depth", defaultValue: null, minimum: 0, maximum: TreeRequestOptions.MaxDepthLimit),
            StringEnum("get_ui_summary", "depthMode", defaultValue: "semantic", "semantic", "visual"),
            Array("capture_state_snapshot", "propertyNames", maxItems: CaptureStateSnapshotTool.MaxSnapshotPropertyNameCount),
            Array("capture_state_snapshot", "viewModelPropertyNames", maxItems: CaptureStateSnapshotTool.MaxSnapshotPropertyNameCount),
            String("capture_state_snapshot", "propertyNames[]", maxLength: CaptureStateSnapshotTool.MaxSnapshotPropertyNameLength),
            String("capture_state_snapshot", "viewModelPropertyNames[]", maxLength: CaptureStateSnapshotTool.MaxSnapshotPropertyNameLength),
            StringEnum("element_screenshot", "outputMode", defaultValue: "metadata", "metadata", "file", "base64"),
            Integer("element_screenshot", "maxWidth", defaultValue: null, minimum: 1, maximum: int.MaxValue),
            Integer("element_screenshot", "maxHeight", defaultValue: null, minimum: 1, maximum: int.MaxValue)
        };
    }

    private static object Integer(string tool, string parameter, int? defaultValue, int? minimum, int? maximum)
        => new
        {
            tool,
            parameter,
            type = "integer",
            defaultValue,
            minimum,
            maximum
        };

    private static object StringEnum(string tool, string parameter, string defaultValue, params string[] allowedValues)
        => new
        {
            tool,
            parameter,
            type = "string",
            defaultValue,
            allowedValues
        };

    private static object Array(string tool, string parameter, int maxItems)
        => new
        {
            tool,
            parameter,
            type = "array",
            maxItems
        };

    private static object String(string tool, string parameter, int maxLength)
        => new
        {
            tool,
            parameter,
            type = "string",
            maxLength
        };
}
