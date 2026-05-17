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

            var visited = new HashSet<DependencyObject>();
            var stack = new Stack<(DependencyObject Element, int Depth)>();
            var yieldedNodeCount = 0;
            stack.Push((_root, 0));

            while (stack.Count > 0)
            {
                var (current, depth) = stack.Pop();

                if (depth > _maxDepth || !visited.Add(current))
                {
                    continue;
                }

                yield return current;
                yieldedNodeCount++;

                if (_maxNodes.HasValue)
                {
                    var remainingBudget = _maxNodes.Value - yieldedNodeCount;
                    if (remainingBudget <= 0)
                    {
                        Truncated |= stack.Count > 0 || HasAnyChild(current);
                        yield break;
                    }

                    PushChildrenWithinBudget(current, depth, remainingBudget, stack);
                    continue;
                }

                PushAllChildren(current, depth, stack);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();

        private void PushChildrenWithinBudget(
            DependencyObject current,
            int depth,
            int remainingBudget,
            Stack<(DependencyObject Element, int Depth)> stack)
        {
            var children = CollectChildrenWithinBudget(current, remainingBudget, out var childOverflow);
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

    private static bool HasAnyChild(DependencyObject current)
    {
        using var enumerator = EnumerateChildren(current).GetEnumerator();
        return enumerator.MoveNext();
    }

    private static List<DependencyObject> CollectChildrenWithinBudget(
        DependencyObject current,
        int remainingBudget,
        out bool truncated)
    {
        truncated = false;
        var children = new List<DependencyObject>(remainingBudget);

        foreach (var child in EnumerateChildren(current))
        {
            if (children.Count >= remainingBudget)
            {
                truncated = true;
                break;
            }

            children.Add(child);
        }

        return children;
    }

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
