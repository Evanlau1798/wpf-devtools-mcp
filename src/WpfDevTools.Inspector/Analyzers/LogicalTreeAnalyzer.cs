using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes and walks the WPF Logical Tree for a given element or the root of the application.
/// Provides logical tree structure including element types, names, and child relationships.
/// All operations are marshalled to the UI thread via <see cref="DispatcherAnalyzerBase"/>.
/// </summary>
public sealed class LogicalTreeAnalyzer : DispatcherAnalyzerBase
{
    private static readonly string[] SummaryColumns = ["elementId", "type", "name", "childCount", "depth", "parentId"];
    private readonly ElementFinder _elementFinder;

    /// <summary>
    /// Initializes a logical tree analyzer backed by the provided element finder.
    /// </summary>
    public LogicalTreeAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Gets the logical tree for the specified element or application root.
    /// </summary>
    public object GetLogicalTree(int? depth, string? elementId)
        => GetLogicalTreeWithOptions(TreeTraversalOptions.Create(depth, compact: null, summaryOnly: null, maxNodes: null, maxChildrenPerNode: null), elementId);

    internal object GetLogicalTreeWithOptions(TreeTraversalOptions options, string? elementId)
    {
        var root = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        return InvokeOnDispatcher<object>(root?.Dispatcher ?? Application.Current?.Dispatcher, () =>
        {
            var resolvedRoot = root ?? (elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId));

            if (resolvedRoot == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var budget = new TreeTraversalBudget(options.MaxNodes);
            budget.TryTakeNode();

            if (options.SummaryOnly)
            {
                var nodes = new List<object?[]>();
                CollectSummary(resolvedRoot, parentId: null, options, currentDepth: 0, budget, nodes);
                return CreateSummaryResult(options, budget, nodes);
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

    private object CreateSummaryResult(
        TreeTraversalOptions options,
        TreeTraversalBudget budget,
        List<object?[]> nodes)
    {
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

    private Dictionary<string, object?> BuildTreeNode(
        DependencyObject element,
        TreeTraversalOptions options,
        int currentDepth,
        TreeTraversalBudget budget)
    {
        var children = GetLogicalChildren(element);
        var node = CreateNodeMap(element, children.Count, options.Compact);

        if (currentDepth >= options.MaxDepth || children.Count == 0)
        {
            return node;
        }

        var childrenToExpand = children.Count;
        var omittedImmediateChildren = 0;
        if (options.MaxChildrenPerNode.HasValue && childrenToExpand > options.MaxChildrenPerNode.Value)
        {
            childrenToExpand = options.MaxChildrenPerNode.Value;
            omittedImmediateChildren = children.Count - childrenToExpand;
            foreach (var omittedChild in children.Skip(childrenToExpand))
            {
                budget.OmitSubtree(CountLogicalSubtree(omittedChild));
            }
        }

        var expandedChildren = new List<object>(childrenToExpand);
        for (var index = 0; index < childrenToExpand; index++)
        {
            var child = children[index];
            if (!budget.TryTakeNode())
            {
                omittedImmediateChildren += childrenToExpand - index;
                for (var remainingIndex = index; remainingIndex < childrenToExpand; remainingIndex++)
                {
                    budget.OmitSubtree(CountLogicalSubtree(children[remainingIndex]));
                }
                break;
            }

            expandedChildren.Add(BuildTreeNode(child, options, currentDepth + 1, budget));
        }

        if (expandedChildren.Count > 0)
        {
            node["children"] = expandedChildren;
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
        var children = GetLogicalChildren(element);
        var name = (element as FrameworkElement)?.Name;

        nodes.Add(new object?[] {
            elementId,
            element.GetType().Name,
            string.IsNullOrEmpty(name) ? null : name,
            children.Count,
            currentDepth,
            parentId
        });

        if (currentDepth >= options.MaxDepth || children.Count == 0)
        {
            return;
        }

        var childrenToExpand = children.Count;
        if (options.MaxChildrenPerNode.HasValue && childrenToExpand > options.MaxChildrenPerNode.Value)
        {
            childrenToExpand = options.MaxChildrenPerNode.Value;
            foreach (var omittedChild in children.Skip(childrenToExpand))
            {
                budget.OmitSubtree(CountLogicalSubtree(omittedChild));
            }
        }

        for (var index = 0; index < childrenToExpand; index++)
        {
            var child = children[index];
            if (!budget.TryTakeNode())
            {
                for (var remainingIndex = index; remainingIndex < childrenToExpand; remainingIndex++)
                {
                    budget.OmitSubtree(CountLogicalSubtree(children[remainingIndex]));
                }
                break;
            }

            CollectSummary(child, elementId, options, currentDepth + 1, budget, nodes);
        }
    }

    private Dictionary<string, object?> CreateNodeMap(
        DependencyObject element,
        int childCount,
        bool compact)
    {
        var elementId = _elementFinder.GenerateElementId(element);
        var node = new Dictionary<string, object?>
        {
            ["elementId"] = elementId,
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

    private List<DependencyObject> GetLogicalChildren(DependencyObject element)
    {
        var children = new List<DependencyObject>();
        foreach (var child in LogicalTreeHelper.GetChildren(element))
        {
            if (child is DependencyObject dependencyObject)
            {
                children.Add(dependencyObject);
            }
        }

        return children;
    }

    private int CountLogicalSubtree(DependencyObject element)
    {
        var count = 1;
        foreach (var child in LogicalTreeHelper.GetChildren(element))
        {
            if (child is DependencyObject dependencyObject)
            {
                count += CountLogicalSubtree(dependencyObject);
            }
        }

        return count;
    }
}
