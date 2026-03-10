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
        int maxDepth = 50)
    {
        var visited = new HashSet<DependencyObject>();
        return EnumerateCore(root, depth: 0, maxDepth, visited);
    }

    private static IEnumerable<DependencyObject> EnumerateCore(
        DependencyObject current,
        int depth,
        int maxDepth,
        HashSet<DependencyObject> visited)
    {
        if (depth > maxDepth || !visited.Add(current))
        {
            yield break;
        }

        yield return current;

        foreach (var child in EnumerateChildren(current))
        {
            foreach (var descendant in EnumerateCore(child, depth + 1, maxDepth, visited))
            {
                yield return descendant;
            }
        }
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
