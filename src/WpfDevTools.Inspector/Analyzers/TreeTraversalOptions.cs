namespace WpfDevTools.Inspector.Analyzers;

internal sealed class TreeTraversalOptions
{
    private const int MaxDepthLimit = 100;

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
            Math.Min(depth ?? 10, MaxDepthLimit),
            compact ?? false,
            summaryOnly ?? false,
            maxNodes,
            maxChildrenPerNode);
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