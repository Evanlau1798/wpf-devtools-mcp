using System.Windows;
using System.Windows.Media;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class PerformanceAnalyzer
{
    private static VisualCountResult CountVisualElements(
        DependencyObject element,
        int maxDepth = TreeTraversalDefaults.MaxDepthLimit,
        int currentDepth = 0,
        int currentCount = 0)
    {
        if (currentCount >= TreeTraversalDefaults.DefaultMaxNodes)
        {
            return new VisualCountResult(
                TreeTraversalDefaults.DefaultMaxNodes,
                TreeTraversalDefaults.DefaultMaxNodes,
                Truncated: true);
        }

        var count = currentCount + 1;
        var truncated = currentDepth >= maxDepth && VisualTreeHelper.GetChildrenCount(element) > 0;
        if (currentDepth >= maxDepth)
        {
            return new VisualCountResult(count, TreeTraversalDefaults.DefaultMaxNodes, truncated);
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            var childResult = CountVisualElements(child, maxDepth, currentDepth + 1, count);
            count = childResult.Count;
            truncated |= childResult.Truncated;

            if (count >= TreeTraversalDefaults.DefaultMaxNodes)
            {
                if (index == childCount - 1 && !truncated)
                {
                    return new VisualCountResult(
                        TreeTraversalDefaults.DefaultMaxNodes,
                        TreeTraversalDefaults.DefaultMaxNodes,
                        Truncated: false);
                }

                return new VisualCountResult(
                    TreeTraversalDefaults.DefaultMaxNodes,
                    TreeTraversalDefaults.DefaultMaxNodes,
                    Truncated: true);
            }
        }

        return new VisualCountResult(count, TreeTraversalDefaults.DefaultMaxNodes, truncated);
    }

    private readonly record struct VisualCountResult(int Count, int Limit, bool Truncated)
    {
        public static VisualCountResult Empty { get; } = new(0, TreeTraversalDefaults.DefaultMaxNodes, false);
    }
}
