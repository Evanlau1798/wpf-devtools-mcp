namespace WpfDevTools.Inspector.Analyzers;

internal sealed class TreeTraversalBudget
{
    private readonly int? _maxNodes;

    public TreeTraversalBudget(int? maxNodes)
    {
        _maxNodes = maxNodes;
    }

    public int ReturnedNodeCount { get; private set; }

    public int OmittedNodeCount { get; private set; }

    public bool Truncated { get; private set; }

    public bool TryTakeNode()
    {
        if (_maxNodes.HasValue && ReturnedNodeCount >= _maxNodes.Value)
        {
            Truncated = true;
            return false;
        }

        ReturnedNodeCount++;
        return true;
    }

    public void OmitSubtree(int nodeCount)
    {
        if (nodeCount <= 0)
        {
            return;
        }

        OmittedNodeCount += nodeCount;
        Truncated = true;
    }
}