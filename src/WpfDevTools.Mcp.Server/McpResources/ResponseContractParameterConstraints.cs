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
            Integer("get_visual_tree", "depth", defaultValue: null, minimum: 0, maximum: TreeRequestOptions.MaxDepthLimit),
            Integer("get_visual_tree", "maxNodes", defaultValue: TreeTraversalDefaults.DefaultMaxNodes, minimum: 1, maximum: TreeRequestOptions.MaxNodesLimit),
            Integer("get_visual_tree", "maxChildrenPerNode", defaultValue: TreeTraversalDefaults.DefaultMaxChildrenPerNode, minimum: 1, maximum: TreeRequestOptions.MaxChildrenPerNodeLimit),
            Integer("get_logical_tree", "depth", defaultValue: null, minimum: 0, maximum: TreeRequestOptions.MaxDepthLimit),
            Integer("get_logical_tree", "maxNodes", defaultValue: TreeTraversalDefaults.DefaultMaxNodes, minimum: 1, maximum: TreeRequestOptions.MaxNodesLimit),
            Integer("get_logical_tree", "maxChildrenPerNode", defaultValue: TreeTraversalDefaults.DefaultMaxChildrenPerNode, minimum: 1, maximum: TreeRequestOptions.MaxChildrenPerNodeLimit),
            Integer("trace_routed_events", "maxEvents", defaultValue: null, minimum: 1, maximum: null),
            Integer("drain_events", "maxEvents", defaultValue: null, minimum: 1, maximum: null),
            Integer("get_binding_errors", "maxErrors", defaultValue: null, minimum: 1, maximum: null)
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
}
