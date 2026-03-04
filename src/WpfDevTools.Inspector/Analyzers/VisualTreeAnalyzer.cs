using System.Windows;
using System.Windows.Media;
using System.Windows.Markup;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Visual Tree
/// </summary>
public class VisualTreeAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    public VisualTreeAnalyzer()
    {
        _elementFinder = new ElementFinder();
    }

    public VisualTreeAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get Visual Tree starting from root or specific element
    /// </summary>
    public object GetVisualTree(int? maxDepth = null, string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            // Get root element
            var root = elementId == null
                ? GetRootElement()
                : _elementFinder.FindById(elementId);

            if (root == null)
            {
                return new { success = false, error = "Root element not found" };
            }

            // Walk tree
            var tree = WalkVisualTree(root, maxDepth ?? int.MaxValue, 0);

            return new { tree };
        });
    }

    private DependencyObject? GetRootElement()
    {
        return _elementFinder.GetRootElement();
    }

    /// <summary>
    /// Compare Visual Tree and Logical Tree to identify discrepancies
    /// </summary>
    public object CompareTree(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return new { success = false, error = "Element not found" };
            }

            var visualChildren = GetVisualChildren(element);
            var logicalChildren = GetLogicalChildren(element);

            var differences = new List<object>();

            // Find elements in visual tree but not in logical tree
            foreach (var visualChild in visualChildren)
            {
                if (!logicalChildren.Any(lc => ReferenceEquals(lc, visualChild)))
                {
                    differences.Add(new
                    {
                        type = "VisualOnly",
                        elementType = visualChild.GetType().Name,
                        elementId = _elementFinder.GenerateElementId(visualChild)
                    });
                }
            }

            // Find elements in logical tree but not in visual tree
            foreach (var logicalChild in logicalChildren)
            {
                if (!visualChildren.Any(vc => ReferenceEquals(vc, logicalChild)))
                {
                    differences.Add(new
                    {
                        type = "LogicalOnly",
                        elementType = logicalChild.GetType().Name,
                        elementId = _elementFinder.GenerateElementId(logicalChild)
                    });
                }
            }

            return new
            {
                success = true,
                visualChildCount = visualChildren.Count,
                logicalChildCount = logicalChildren.Count,
                differenceCount = differences.Count,
                differences
            };
        });
    }

    /// <summary>
    /// Get NameScope information for an element
    /// </summary>
    public object GetNameScope(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return new { success = false, error = "Element not found" };
            }

            var nameScope = NameScope.GetNameScope(element);
            var namedElements = new List<object>();

            if (nameScope != null)
            {
                // Get all named elements in this scope
                var names = GetNamesInScope(element);

                foreach (var name in names)
                {
                    var namedElement = nameScope.FindName(name);
                    if (namedElement != null)
                    {
                        var depObj = namedElement as DependencyObject;
                        namedElements.Add(new
                        {
                            name,
                            type = namedElement.GetType().Name,
                            elementId = depObj != null ? _elementFinder.GenerateElementId(depObj) : null
                        });
                    }
                }
            }

            return new
            {
                success = true,
                hasNameScope = nameScope != null,
                namedElementCount = namedElements.Count,
                namedElements
            };
        });
    }

    private List<string> GetNamesInScope(DependencyObject element)
    {
        var names = new List<string>();

        // Recursively find all named elements
        if (element is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
        {
            names.Add(fe.Name);
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            names.AddRange(GetNamesInScope(child));
        }

        return names;
    }

    private List<DependencyObject> GetVisualChildren(DependencyObject element)
    {
        var children = new List<DependencyObject>();
        var count = VisualTreeHelper.GetChildrenCount(element);

        for (int i = 0; i < count; i++)
        {
            children.Add(VisualTreeHelper.GetChild(element, i));
        }

        return children;
    }

    private List<DependencyObject> GetLogicalChildren(DependencyObject element)
    {
        var children = new List<DependencyObject>();

        if (element is FrameworkElement fe)
        {
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(fe))
            {
                if (child is DependencyObject depObj)
                {
                    children.Add(depObj);
                }
            }
        }

        return children;
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
