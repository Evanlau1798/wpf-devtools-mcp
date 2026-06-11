using System.Windows;
using System.Windows.Media;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class PerformanceAnalyzer
{
    private static VisualCountResult CountVisualElements(
        DependencyObject element,
        int? maxNodes = null,
        int maxDepth = TreeTraversalDefaults.MaxDepthLimit,
        int currentDepth = 0,
        int currentCount = 0)
    {
        if (maxNodes.HasValue && currentCount >= maxNodes.Value)
        {
            return new VisualCountResult(
                maxNodes.Value,
                maxNodes.Value,
                Truncated: true);
        }

        var count = currentCount + 1;
        var truncated = currentDepth >= maxDepth && VisualTreeHelper.GetChildrenCount(element) > 0;
        if (currentDepth >= maxDepth)
        {
            return new VisualCountResult(count, maxNodes ?? count, truncated);
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            var childResult = CountVisualElements(child, maxNodes, maxDepth, currentDepth + 1, count);
            count = childResult.Count;
            truncated |= childResult.Truncated;

            if (maxNodes.HasValue && count >= maxNodes.Value)
            {
                if (index == childCount - 1 && !truncated)
                {
                    return new VisualCountResult(
                        maxNodes.Value,
                        maxNodes.Value,
                        Truncated: false);
                }

                return new VisualCountResult(
                    maxNodes.Value,
                    maxNodes.Value,
                    Truncated: true);
            }
        }

        return new VisualCountResult(count, maxNodes ?? count, truncated);
    }

    private readonly record struct VisualCountResult(int Count, int Limit, bool Truncated)
    {
        public static VisualCountResult Empty { get; } = new(0, TreeTraversalDefaults.DefaultMaxNodes, false);
    }
}
