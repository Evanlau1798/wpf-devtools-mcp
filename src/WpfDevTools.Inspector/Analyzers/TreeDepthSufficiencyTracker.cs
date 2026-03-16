namespace WpfDevTools.Inspector.Analyzers;

internal sealed class TreeDepthSufficiencyTracker
{
    private readonly int _currentDepthLimit;
    private bool _hitDepthLimitWithHiddenChildren;

    public TreeDepthSufficiencyTracker(int currentDepthLimit)
    {
        _currentDepthLimit = currentDepthLimit;
    }

    public void MarkDepthLimitedBranch()
    {
        _hitDepthLimitWithHiddenChildren = true;
    }

    public object? BuildHint()
    {
        if (!_hitDepthLimitWithHiddenChildren)
        {
            return null;
        }

        return new
        {
            isSufficient = false,
            reasonCode = "depthLimitReached",
            currentDepth = _currentDepthLimit,
            recommendedDepth = _currentDepthLimit + 2,
            suggestion = "Increase depth and retry, or pivot to get_ui_summary/find_elements for semantic-first narrowing."
        };
    }
}
