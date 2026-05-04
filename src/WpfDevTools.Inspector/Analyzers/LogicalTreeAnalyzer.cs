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
        : base(elementFinder)
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
        var root = ResolveElement(elementId);

        if (root == null)
        {
            return ToolErrorFactory.ElementNotFound(elementId);
        }

        return InvokeOnDispatcher<object>(root.Dispatcher, () =>
        {
            var resolvedRoot = root;

            if (resolvedRoot == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var budget = new TreeTraversalBudget(options.MaxNodes);
            budget.TryTakeNode();
            var depthHintTracker = new TreeDepthSufficiencyTracker(options.MaxDepth);

            if (options.SummaryOnly)
            {
                var nodes = new List<object?[]>();
                CollectSummary(resolvedRoot, parentId: null, options, currentDepth: 0, budget, nodes, depthHintTracker);
                return CreateSummaryResult(options, budget, nodes, depthHintTracker.BuildHint());
            }

            var tree = BuildTreeNode(resolvedRoot, options, currentDepth: 0, budget, depthHintTracker);
            return new
            {
                success = true,
                tree,
                depthSufficiencyHint = depthHintTracker.BuildHint(),
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
        List<object?[]> nodes,
        object? depthSufficiencyHint)
    {
        return new
        {
            success = true,
            format = "flat-summary-v1",
            columns = SummaryColumns,
            nodes,
            depthSufficiencyHint,
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
        TreeTraversalBudget budget,
        TreeDepthSufficiencyTracker depthHintTracker)
    {
        var children = GetLogicalChildren(element);
        var node = CreateNodeMap(element, children.Count, options.Compact);

        if (currentDepth >= options.MaxDepth)
        {
            if (children.Count > 0)
            {
                depthHintTracker.MarkDepthLimitedBranch();
            }

            return node;
        }

        if (children.Count == 0)
        {
            return node;
        }

        var childrenToExpand = children.Count;
        var omittedImmediateChildren = 0;
        if (options.MaxChildrenPerNode.HasValue && childrenToExpand > options.MaxChildrenPerNode.Value)
        {
            childrenToExpand = options.MaxChildrenPerNode.Value;
            omittedImmediateChildren = children.Count - childrenToExpand;
            budget.OmitSubtree(omittedImmediateChildren);
        }

        var expandedChildren = new List<object>(childrenToExpand);
        for (var index = 0; index < childrenToExpand; index++)
        {
            var child = children[index];
            if (!budget.TryTakeNode())
            {
                var remainingImmediateChildren = childrenToExpand - index;
                omittedImmediateChildren += remainingImmediateChildren;
                budget.OmitSubtree(remainingImmediateChildren);
                break;
            }

            expandedChildren.Add(BuildTreeNode(child, options, currentDepth + 1, budget, depthHintTracker));
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
        List<object?[]> nodes,
        TreeDepthSufficiencyTracker depthHintTracker)
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

        if (currentDepth >= options.MaxDepth)
        {
            if (children.Count > 0)
            {
                depthHintTracker.MarkDepthLimitedBranch();
            }

            return;
        }

        if (children.Count == 0)
        {
            return;
        }

        var childrenToExpand = children.Count;
        if (options.MaxChildrenPerNode.HasValue && childrenToExpand > options.MaxChildrenPerNode.Value)
        {
            childrenToExpand = options.MaxChildrenPerNode.Value;
            budget.OmitSubtree(children.Count - childrenToExpand);
        }

        for (var index = 0; index < childrenToExpand; index++)
        {
            var child = children[index];
            if (!budget.TryTakeNode())
            {
                budget.OmitSubtree(childrenToExpand - index);
                break;
            }

            CollectSummary(child, elementId, options, currentDepth + 1, budget, nodes, depthHintTracker);
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

}
