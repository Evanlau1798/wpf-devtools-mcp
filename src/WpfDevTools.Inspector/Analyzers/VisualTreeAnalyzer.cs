using System.Windows;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Visual Tree
/// </summary>
public class VisualTreeAnalyzer
{
    /// <summary>
    /// Get Visual Tree starting from root or specific element
    /// </summary>
    public object GetVisualTree(int? maxDepth = null, string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetVisualTree(maxDepth, elementId));
        }

        // Get root element
        var root = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (root == null)
        {
            return new { error = "Root element not found" };
        }

        // Walk tree
        var tree = WalkVisualTree(root, maxDepth ?? int.MaxValue, 0);

        return new { tree };
    }

    private DependencyObject? GetRootElement()
    {
        // Get main window
        return Application.Current?.MainWindow;
    }

    private DependencyObject? FindElementById(string elementId)
    {
        // TODO: Implement element lookup by ID
        // For now, return main window
        return GetRootElement();
    }

    private object WalkVisualTree(DependencyObject element, int maxDepth, int currentDepth)
    {
        if (currentDepth >= maxDepth)
        {
            return new { type = element.GetType().Name, childCount = VisualTreeHelper.GetChildrenCount(element) };
        }

        var children = new List<object>();
        var childCount = VisualTreeHelper.GetChildrenCount(element);

        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            children.Add(WalkVisualTree(child, maxDepth, currentDepth + 1));
        }

        return new
        {
            type = element.GetType().Name,
            name = (element as FrameworkElement)?.Name,
            childCount = childCount,
            children = children.Count > 0 ? children : null
        };
    }
}
