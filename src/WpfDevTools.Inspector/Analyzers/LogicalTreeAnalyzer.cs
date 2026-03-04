using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public class LogicalTreeAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    public LogicalTreeAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    public object GetLogicalTree(int? depth, string? elementId)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var root = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (root == null)
            {
                return new { success = false, error = "Element not found" };
            }

            var tree = WalkLogicalTree(root, depth ?? 10, 0);
            return new { tree };
        });
    }

    private object WalkLogicalTree(DependencyObject element, int maxDepth, int currentDepth)
    {
        if (currentDepth >= maxDepth)
        {
            return new { type = element.GetType().Name };
        }

        var children = new List<object>();
        foreach (var child in LogicalTreeHelper.GetChildren(element))
        {
            if (child is DependencyObject depObj)
            {
                children.Add(WalkLogicalTree(depObj, maxDepth, currentDepth + 1));
            }
        }

        return new
        {
            type = element.GetType().Name,
            name = (element as FrameworkElement)?.Name,
            childCount = children.Count,
            children = children.Count > 0 ? children : null
        };
    }
}
