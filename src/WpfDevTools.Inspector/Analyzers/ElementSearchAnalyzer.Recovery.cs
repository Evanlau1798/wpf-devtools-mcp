using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class ElementSearchAnalyzer
{
    private static int ResolveTraversalLimit(int? maxTraversalNodes)
    {
        var resolved = maxTraversalNodes ?? TreeTraversalDefaults.DefaultMaxNodes;
        return Math.Max(1, Math.Min(resolved, TreeTraversalDefaults.MaxNodesLimit));
    }

    private static object? CreateTraversalRecovery(
        bool traversalTruncated,
        int resultCount,
        int traversalLimit)
        => traversalTruncated && resultCount == 0
            ? new
            {
                code = "TraversalBudgetExceededBeforeMatch",
                message = "No match was found before the bounded traversal ended; the zero-result response is inconclusive.",
                retry = new
                {
                    parameter = "maxTraversalNodes",
                    canIncrease = traversalLimit < TreeTraversalDefaults.MaxNodesLimit,
                    suggestedValue = traversalLimit < TreeTraversalDefaults.MaxNodesLimit
                        ? Math.Min(traversalLimit * 2, TreeTraversalDefaults.MaxNodesLimit)
                        : (int?)null
                },
                alternative = new
                {
                    parameter = "elementId",
                    guidance = "Use get_ui_summary or a bounded tree read to choose a narrower ancestor, then retry within that element."
                }
            }
            : null;
}
