using System.Windows;
using System.Windows.Controls;
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

    internal VisualTreeAnalyzer()
    {
        _elementFinder = new ElementFinder();
    }

    /// <summary>
    /// Create a new VisualTreeAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
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

            // Walk tree with hard upper limit of 100
            var effectiveDepth = Math.Min(maxDepth ?? 50, 100);
            var tree = WalkVisualTree(root, effectiveDepth, 0);

            return new { success = true, tree };
        });
    }

    private DependencyObject? GetRootElement()
    {
        return _elementFinder.GetRootElement();
    }

    /// <summary>
    /// Compare Visual Tree and Logical Tree to identify discrepancies
    /// Optimized with HashSet for O(n+m) instead of O(n*m)
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

            // Use HashSet for O(1) lookups instead of O(n) Any() calls
            var visualSet = new HashSet<DependencyObject>(visualChildren, ReferenceEqualityComparer.Instance);
            var logicalSet = new HashSet<DependencyObject>(logicalChildren, ReferenceEqualityComparer.Instance);

            var differences = new List<object>();

            // Find elements in visual tree but not in logical tree - O(n)
            foreach (var visualChild in visualChildren)
            {
                if (!logicalSet.Contains(visualChild))
                {
                    differences.Add(new
                    {
                        type = "VisualOnly",
                        elementType = visualChild.GetType().Name,
                        elementId = _elementFinder.GenerateElementId(visualChild)
                    });
                }
            }

            // Find elements in logical tree but not in visual tree - O(m)
            foreach (var logicalChild in logicalChildren)
            {
                if (!visualSet.Contains(logicalChild))
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
                var names = new List<string>();
                GetNamesInScope(element, names);

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

    /// <summary>
    /// Optimized: Pass list as parameter instead of returning and merging
    /// Avoids creating intermediate List objects at each recursion level
    /// </summary>
    private void GetNamesInScope(DependencyObject element, List<string> names)
    {
        // Add current element's name if it has one
        if (element is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
        {
            names.Add(fe.Name);
        }

        // Recursively collect names from children
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            GetNamesInScope(child, names);
        }
    }

    /// <summary>
    /// Get template Visual Tree of a templated control
    /// </summary>
    public object GetTemplateTree(string? elementId, int? maxDepth = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (string.IsNullOrEmpty(elementId))
            {
                return new { success = false, error = "elementId is required for get_template_tree" };
            }

            var element = _elementFinder.FindById(elementId);
            if (element == null)
            {
                return new { success = false, error = "Element not found" };
            }

            if (element is not Control control || control.Template == null)
            {
                return new { success = false, error = "Element is not a templated control or has no template" };
            }

            var templateRoot = VisualTreeHelper.GetChildrenCount(control) > 0
                ? VisualTreeHelper.GetChild(control, 0)
                : null;

            if (templateRoot == null)
            {
                return new { success = false, error = "No template visual tree found" };
            }

            var effectiveDepth = Math.Min(maxDepth ?? 10, 100);
            var tree = WalkVisualTree(templateRoot, effectiveDepth, 0);
            return new { success = true, tree };
        });
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
        var elementId = _elementFinder.GenerateElementId(element);

        if (currentDepth >= maxDepth)
        {
            return new
            {
                elementId,
                type = element.GetType().Name,
                childCount = VisualTreeHelper.GetChildrenCount(element)
            };
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
            elementId,
            type = element.GetType().Name,
            name = (element as FrameworkElement)?.Name,
            childCount = childCount,
            children = children.Count > 0 ? children : null
        };
    }
}

/// <summary>
/// Reference equality comparer for HashSet
/// Uses reference equality instead of value equality for DependencyObject comparisons
/// </summary>
internal class ReferenceEqualityComparer : IEqualityComparer<DependencyObject>
{
    public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

    public bool Equals(DependencyObject? x, DependencyObject? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(DependencyObject obj)
    {
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
