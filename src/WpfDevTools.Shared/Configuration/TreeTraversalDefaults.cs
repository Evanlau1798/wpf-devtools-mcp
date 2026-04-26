namespace WpfDevTools.Shared.Configuration;

/// <summary>
/// Shared traversal limits used to keep tree-shaped MCP payloads bounded by default.
/// </summary>
public static class TreeTraversalDefaults
{
    /// <summary>Maximum accepted traversal depth for tree-shaped diagnostics.</summary>
    public const int MaxDepthLimit = 100;

    /// <summary>Largest caller-requested node cap accepted by MCP tree tools.</summary>
    public const int MaxNodesLimit = 10000;

    /// <summary>Largest caller-requested per-node child fan-out accepted by MCP tree tools.</summary>
    public const int MaxChildrenPerNodeLimit = 1000;

    /// <summary>Default node cap applied when callers omit maxNodes.</summary>
    public const int DefaultMaxNodes = 1000;

    /// <summary>Default per-node fan-out cap applied when callers omit maxChildrenPerNode.</summary>
    public const int DefaultMaxChildrenPerNode = 200;
}
