using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public class LogicalTreeAnalyzer
{
    private readonly ElementFinder _elementFinder;

    public LogicalTreeAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    public object GetLogicalTree(int? depth, string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetLogicalTree(depth, elementId));
        }

        var root = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (root == null)
        {
            return new { error = "Element not found" };
        }

        var tree = WalkLogicalTree(root, depth ?? 10, 0);
        return new { tree };
    }

    private object WalkLogicalTree(DependencyObject element, int maxDepth, int currentDepth)
    {
        // TODO: Implement
        return new { type = element.GetType().Name };
    }
}
