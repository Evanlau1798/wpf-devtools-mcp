using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfDevTools.Inspector.Utilities;

internal static class DependencyObjectTraversal
{
    /// <summary>
    /// Maximum number of items to traverse from an ItemsControl.
    /// Prevents unbounded iteration on large DataGrid/ListBox scenarios.
    /// </summary>
    private const int MaxItemsControlChildren = 1000;

    public static IEnumerable<DependencyObject> EnumerateDescendantsAndSelf(
        DependencyObject root,
        int maxDepth = 50,
        int? maxNodes = null)
        => EnumerateDescendantsAndSelfWithMetadata(root, maxDepth, maxNodes);

    internal static DependencyObjectTraversalResult EnumerateDescendantsAndSelfWithMetadata(
        DependencyObject root,
        int maxDepth = 50,
        int? maxNodes = null)
        => new(root, maxDepth, maxNodes);

    internal sealed class DependencyObjectTraversalResult : IEnumerable<DependencyObject>
    {
        private readonly DependencyObject _root;
        private readonly int _maxDepth;
        private readonly int? _maxNodes;

        public DependencyObjectTraversalResult(DependencyObject root, int maxDepth, int? maxNodes)
        {
            _root = root;
            _maxDepth = maxDepth;
            _maxNodes = maxNodes;
        }

        public bool Truncated { get; private set; }

        public IEnumerator<DependencyObject> GetEnumerator()
        {
            Truncated = false;
            if (_maxNodes is <= 0)
            {
                Truncated = true;
                yield break;
            }

            var yielded = new HashSet<DependencyObject>();
            var expandedDepths = new Dictionary<DependencyObject, int>();
            var depthPrunedCandidates = new HashSet<DependencyObject>();
            var stack = new Stack<(DependencyObject Element, int Depth)>();
            var yieldedNodeCount = 0;
            stack.Push((_root, 0));

            while (stack.Count > 0)
            {
                var (current, depth) = stack.Pop();

                if (depth > _maxDepth)
                {
                    if (!yielded.Contains(current))
                    {
                        depthPrunedCandidates.Add(current);
                    }

                    continue;
                }

                if (!CanImproveExpansionDepth(current, depth, expandedDepths))
                {
                    continue;
                }

                expandedDepths[current] = depth;
                depthPrunedCandidates.Remove(current);

                if (yielded.Add(current))
                {
                    yield return current;
                    yieldedNodeCount++;
                }

                if (_maxNodes.HasValue)
                {
                    var remainingBudget = _maxNodes.Value - yieldedNodeCount;
                    if (remainingBudget <= 0)
                    {
                        Truncated |= depthPrunedCandidates.Count > 0 ||
                            HasAnyPendingStackWork(stack, yielded, expandedDepths) ||
                            HasAnyPendingChildWork(current, depth, yielded, expandedDepths);
                        yield break;
                    }

                    PushChildrenWithinBudget(current, depth, remainingBudget, stack, yielded, expandedDepths);
                    continue;
                }

                PushAllChildren(current, depth, stack);
            }

            Truncated |= depthPrunedCandidates.Count > 0;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();

        private void PushChildrenWithinBudget(
            DependencyObject current,
            int depth,
            int remainingBudget,
            Stack<(DependencyObject Element, int Depth)> stack,
            HashSet<DependencyObject> yielded,
            Dictionary<DependencyObject, int> expandedDepths)
        {
            var children = CollectChildrenWithinBudget(current, depth, remainingBudget, yielded, expandedDepths, out var childOverflow);
            Truncated |= childOverflow;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i], depth + 1));
            }
        }

        private static void PushAllChildren(
            DependencyObject current,
            int depth,
            Stack<(DependencyObject Element, int Depth)> stack)
        {
            var children = EnumerateChildren(current).ToList();
            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i], depth + 1));
            }
        }
    }

    private static bool HasAnyPendingStackWork(
        Stack<(DependencyObject Element, int Depth)> stack,
        HashSet<DependencyObject> yielded,
        Dictionary<DependencyObject, int> expandedDepths)
    {
        foreach (var (element, depth) in stack)
        {
            if (!yielded.Contains(element) || CanImproveExpansionDepth(element, depth, expandedDepths))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyPendingChildWork(
        DependencyObject current,
        int depth,
        HashSet<DependencyObject> yielded,
        Dictionary<DependencyObject, int> expandedDepths)
    {
        using var enumerator = EnumerateChildren(current).GetEnumerator();
        while (enumerator.MoveNext())
        {
            var childDepth = depth + 1;
            if (!yielded.Contains(enumerator.Current) ||
                CanImproveExpansionDepth(enumerator.Current, childDepth, expandedDepths))
            {
                return true;
            }
        }

        return false;
    }

    private static List<DependencyObject> CollectChildrenWithinBudget(
        DependencyObject current,
        int depth,
        int remainingBudget,
        HashSet<DependencyObject> yielded,
        Dictionary<DependencyObject, int> expandedDepths,
        out bool truncated)
    {
        truncated = false;
        var children = new List<DependencyObject>(remainingBudget);
        var unyieldedChildrenCount = 0;

        foreach (var child in EnumerateChildren(current))
        {
            var childDepth = depth + 1;
            var childAlreadyYielded = yielded.Contains(child);
            if (childAlreadyYielded && !CanImproveExpansionDepth(child, childDepth, expandedDepths))
            {
                continue;
            }

            if (!childAlreadyYielded)
            {
                if (unyieldedChildrenCount >= remainingBudget)
                {
                    truncated = true;
                    break;
                }

                unyieldedChildrenCount++;
            }

            children.Add(child);
        }

        return children;
    }

    private static bool CanImproveExpansionDepth(
        DependencyObject element,
        int depth,
        Dictionary<DependencyObject, int> expandedDepths)
        => !expandedDepths.TryGetValue(element, out var bestDepth) || depth < bestDepth;

    internal static IEnumerable<DependencyObject> EnumerateChildren(DependencyObject element)
    {
        var yielded = new HashSet<DependencyObject>();

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            if (yielded.Add(logicalChild))
            {
                yield return logicalChild;
            }
        }

        // WPF's ContentPresenter may re-parent ContentControl.Content in the logical tree,
        // causing LogicalTreeHelper.GetChildren() to miss it (e.g., selected TabItem content).
        // Explicitly yield Content to ensure traversal always reaches it.
        if (element is ContentControl cc && cc.Content is DependencyObject contentObj && yielded.Add(contentObj))
        {
            yield return contentObj;
        }

        // Similarly, HeaderedContentControl.Header may be re-parented by template parts.
        if (element is HeaderedContentControl hcc && hcc.Header is DependencyObject headerObj && yielded.Add(headerObj))
        {
            yield return headerObj;
        }

        // ItemsControl items may not appear in either tree when virtualized.
        // Cap iteration to prevent unbounded traversal on large collections.
        if (element is ItemsControl itemsControl)
        {
            var itemCount = Math.Min(itemsControl.Items.Count, MaxItemsControlChildren);
            for (var i = 0; i < itemCount; i++)
            {
                if (itemsControl.Items[i] is DependencyObject itemObj && yielded.Add(itemObj))
                {
                    yield return itemObj;
                }
            }
        }

        if (element is not Visual && element is not Visual3D)
        {
            yield break;
        }

        int childCount;
        try
        {
            childCount = VisualTreeHelper.GetChildrenCount(element);
        }
        catch (InvalidOperationException)
        {
            yield break;
        }

        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (yielded.Add(child))
            {
                yield return child;
            }
        }
    }
}
