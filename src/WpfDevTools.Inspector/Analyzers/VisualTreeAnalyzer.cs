using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Visual Tree
/// </summary>
public sealed class VisualTreeAnalyzer : DispatcherAnalyzerBase
{
    private static readonly string[] SummaryColumns = ["elementId", "type", "name", "childCount", "depth", "parentId"];
    private readonly ElementFinder _elementFinder;

    internal VisualTreeAnalyzer()
    {
        _elementFinder = new ElementFinder();
    }

    /// <summary>
    /// Initializes a visual tree analyzer backed by the provided element finder.
    /// </summary>
    public VisualTreeAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Gets the visual tree for the specified element or application root.
    /// </summary>
    public object GetVisualTree(int? maxDepth = null, string? elementId = null)
        => GetVisualTreeWithOptions(TreeTraversalOptions.Create(maxDepth, compact: null, summaryOnly: null, maxNodes: null, maxChildrenPerNode: null), elementId);

    internal object GetVisualTreeWithOptions(TreeTraversalOptions options, string? elementId = null)
    {
        var root = elementId == null
            ? GetRootElement()
            : _elementFinder.FindById(elementId);

        return InvokeOnDispatcher<object>(root?.Dispatcher ?? Application.Current?.Dispatcher, () =>
        {
            var resolvedRoot = root ?? (elementId == null
                ? GetRootElement()
                : _elementFinder.FindById(elementId));

            if (resolvedRoot == null)
            {
                return new { success = false, error = "Root element not found" };
            }

            var budget = new TreeTraversalBudget(options.MaxNodes);
            budget.TryTakeNode();

            if (options.SummaryOnly)
            {
                var nodes = new List<object?[]>();
                CollectSummary(resolvedRoot, parentId: null, options, currentDepth: 0, budget, nodes);
                return new
                {
                    success = true,
                    format = "flat-summary-v1",
                    columns = SummaryColumns,
                    nodes,
                    returnedNodeCount = budget.ReturnedNodeCount,
                    omittedNodeCount = budget.OmittedNodeCount,
                    truncated = budget.Truncated,
                    appliedOptions = options.ToAppliedOptions()
                };
            }

            var tree = BuildTreeNode(resolvedRoot, options, currentDepth: 0, budget);
            return new
            {
                success = true,
                tree,
                returnedNodeCount = budget.ReturnedNodeCount,
                omittedNodeCount = budget.OmittedNodeCount,
                truncated = budget.Truncated,
                appliedOptions = options.ToAppliedOptions()
            };
        });
    }

    private DependencyObject? GetRootElement()
    {
        return _elementFinder.GetRootElement();
    }

    /// <summary>
    /// Compares the immediate visual and logical children of the specified element.
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
            var visualSet = new HashSet<DependencyObject>(visualChildren, ReferenceEqualityComparer.Instance);
            var logicalSet = new HashSet<DependencyObject>(logicalChildren, ReferenceEqualityComparer.Instance);
            var differences = new List<object>();

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
    /// Gets the current XAML namescope entries for the specified element.
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

    private void GetNamesInScope(DependencyObject element, List<string> names)
    {
        if (element is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
        {
            names.Add(fe.Name);
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            GetNamesInScope(child, names);
        }
    }

    /// <summary>
    /// Gets the template visual tree for the specified templated control.
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

            var budget = new TreeTraversalBudget(maxNodes: null);
            budget.TryTakeNode();
            var options = TreeTraversalOptions.Create(maxDepth, compact: null, summaryOnly: null, maxNodes: null, maxChildrenPerNode: null);
            var tree = BuildTreeNode(templateRoot, options, currentDepth: 0, budget);
            return new { success = true, tree };
        });
    }

    private Dictionary<string, object?> BuildTreeNode(
        DependencyObject element,
        TreeTraversalOptions options,
        int currentDepth,
        TreeTraversalBudget budget)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        var node = CreateNodeMap(element, childCount, options.Compact);

        if (currentDepth >= options.MaxDepth || childCount == 0)
        {
            return node;
        }

        var childrenToExpand = childCount;
        var omittedImmediateChildren = 0;
        if (options.MaxChildrenPerNode.HasValue && childrenToExpand > options.MaxChildrenPerNode.Value)
        {
            childrenToExpand = options.MaxChildrenPerNode.Value;
            omittedImmediateChildren = childCount - childrenToExpand;
            for (var omittedIndex = childrenToExpand; omittedIndex < childCount; omittedIndex++)
            {
                budget.OmitSubtree(CountVisualSubtree(VisualTreeHelper.GetChild(element, omittedIndex)));
            }
        }

        var children = new List<object>(childrenToExpand);
        for (var index = 0; index < childrenToExpand; index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (!budget.TryTakeNode())
            {
                omittedImmediateChildren += childrenToExpand - index;
                for (var remainingIndex = index; remainingIndex < childrenToExpand; remainingIndex++)
                {
                    budget.OmitSubtree(CountVisualSubtree(VisualTreeHelper.GetChild(element, remainingIndex)));
                }
                break;
            }

            children.Add(BuildTreeNode(child, options, currentDepth + 1, budget));
        }

        if (children.Count > 0)
        {
            node["children"] = children;
        }
        else if (!options.Compact)
        {
            node["children"] = null;
        }

        if (omittedImmediateChildren > 0)
        {
            node["omittedChildCount"] = omittedImmediateChildren;
        }

        return node;
    }

    private void CollectSummary(
        DependencyObject element,
        string? parentId,
        TreeTraversalOptions options,
        int currentDepth,
        TreeTraversalBudget budget,
        List<object?[]> nodes)
    {
        var elementId = _elementFinder.GenerateElementId(element);
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        var name = (element as FrameworkElement)?.Name;

        nodes.Add(new object?[] {
            elementId,
            element.GetType().Name,
            string.IsNullOrEmpty(name) ? null : name,
            childCount,
            currentDepth,
            parentId
        });

        if (currentDepth >= options.MaxDepth || childCount == 0)
        {
            return;
        }

        var childrenToExpand = childCount;
        if (options.MaxChildrenPerNode.HasValue && childrenToExpand > options.MaxChildrenPerNode.Value)
        {
            childrenToExpand = options.MaxChildrenPerNode.Value;
            for (var omittedIndex = childrenToExpand; omittedIndex < childCount; omittedIndex++)
            {
                budget.OmitSubtree(CountVisualSubtree(VisualTreeHelper.GetChild(element, omittedIndex)));
            }
        }

        for (var index = 0; index < childrenToExpand; index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (!budget.TryTakeNode())
            {
                for (var remainingIndex = index; remainingIndex < childrenToExpand; remainingIndex++)
                {
                    budget.OmitSubtree(CountVisualSubtree(VisualTreeHelper.GetChild(element, remainingIndex)));
                }
                break;
            }

            CollectSummary(child, elementId, options, currentDepth + 1, budget, nodes);
        }
    }

    private Dictionary<string, object?> CreateNodeMap(DependencyObject element, int childCount, bool compact)
    {
        var node = new Dictionary<string, object?>
        {
            ["elementId"] = _elementFinder.GenerateElementId(element),
            ["type"] = element.GetType().Name,
            ["childCount"] = childCount
        };

        var name = (element as FrameworkElement)?.Name;
        if (!compact || !string.IsNullOrEmpty(name))
        {
            node["name"] = name;
        }

        return node;
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

    private int CountVisualSubtree(DependencyObject element)
    {
        var count = 1;
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            count += CountVisualSubtree(VisualTreeHelper.GetChild(element, index));
        }

        return count;
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
