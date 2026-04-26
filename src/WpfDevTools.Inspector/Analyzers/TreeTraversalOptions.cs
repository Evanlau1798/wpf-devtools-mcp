using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

internal sealed class TreeTraversalOptions
{
    public int MaxDepth { get; }
    public bool Compact { get; }
    public bool SummaryOnly { get; }
    public int? MaxNodes { get; }
    public int? MaxChildrenPerNode { get; }

    private TreeTraversalOptions(
        int maxDepth,
        bool compact,
        bool summaryOnly,
        int? maxNodes,
        int? maxChildrenPerNode)
    {
        MaxDepth = maxDepth;
        Compact = compact;
        SummaryOnly = summaryOnly;
        MaxNodes = maxNodes;
        MaxChildrenPerNode = maxChildrenPerNode;
    }

    public static TreeTraversalOptions Create(
        int? depth,
        bool? compact,
        bool? summaryOnly,
        int? maxNodes,
        int? maxChildrenPerNode)
    {
        return new TreeTraversalOptions(
            Math.Max(0, Math.Min(depth ?? 10, TreeTraversalDefaults.MaxDepthLimit)),
            compact ?? false,
            summaryOnly ?? false,
            NormalizeCap(maxNodes, TreeTraversalDefaults.DefaultMaxNodes, TreeTraversalDefaults.MaxNodesLimit),
            NormalizeCap(maxChildrenPerNode, TreeTraversalDefaults.DefaultMaxChildrenPerNode, TreeTraversalDefaults.MaxChildrenPerNodeLimit));
    }

    private static int NormalizeCap(int? value, int defaultValue, int upperLimit)
    {
        var resolved = value ?? defaultValue;
        return Math.Max(1, Math.Min(resolved, upperLimit));
    }

    public object ToAppliedOptions()
    {
        return new
        {
            depth = MaxDepth,
            compact = Compact,
            summaryOnly = SummaryOnly,
            maxNodes = MaxNodes,
            maxChildrenPerNode = MaxChildrenPerNode
        };
    }
}
