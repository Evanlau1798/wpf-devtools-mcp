using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfDevTools.Inspector.Utilities;

internal static class DependencyObjectTraversal
{
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

    private static IEnumerable<DependencyObject> EnumerateChildren(DependencyObject element)
    {
        var yielded = new HashSet<DependencyObject>();

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            if (yielded.Add(logicalChild))
            {
                yield return logicalChild;
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
