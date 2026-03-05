using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes and walks the WPF Logical Tree for a given element or the root of the application.
/// Provides logical tree structure including element types, names, and child relationships.
/// All operations are marshalled to the UI thread via <see cref="DispatcherAnalyzerBase"/>.
/// </summary>
public class LogicalTreeAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    /// <summary>
    /// Initializes a new instance of <see cref="LogicalTreeAnalyzer"/> with the specified element finder.
    /// </summary>
    /// <param name="elementFinder">The element finder used to resolve elements by ID or as the root element.</param>
    public LogicalTreeAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Retrieves the Logical Tree structure starting from the specified element or the application root.
    /// </summary>
    /// <param name="depth">Maximum tree depth to traverse. Defaults to 10 when null.</param>
    /// <param name="elementId">Optional element ID to use as the tree root. Uses the application root when null.</param>
    /// <returns>
    /// An object with <c>success: true</c> and a <c>tree</c> property on success,
    /// or <c>success: false</c> and an <c>error</c> message if the element is not found.
    /// </returns>
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
            return new { success = true, tree };
        });
    }

    private object WalkLogicalTree(DependencyObject element, int maxDepth, int currentDepth)
    {
        var elementId = _elementFinder.GenerateElementId(element);

        if (currentDepth >= maxDepth)
        {
            return new { elementId, type = element.GetType().Name };
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
            elementId,
            type = element.GetType().Name,
            name = (element as FrameworkElement)?.Name,
            childCount = children.Count,
            children = children.Count > 0 ? children : null
        };
    }
}
