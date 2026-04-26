using System.Text;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Produces a token-efficient semantic summary of a WPF subtree.
/// </summary>
public sealed class UiSummaryAnalyzer : DispatcherAnalyzerBase
{
    private const int MaxTraversalNodes = 512;
    private const int MaxSemanticNodes = 128;
    private const int MaxSummaryTextLength = 16 * 1024;
    private const int MaxStringValueLength = 512;

    private readonly ElementFinder _elementFinder;

    /// <summary>
    /// Create a UI summary analyzer backed by the shared element finder.
    /// </summary>
    public UiSummaryAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Build a semantic UI summary for the root window or a scoped subtree.
    /// </summary>
    public object GetUiSummary(
        string? elementId = null,
        int? depth = null,
        string? depthMode = null,
        bool summaryOnly = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (depth is < 0)
            {
                return ToolErrorFactory.InvalidArgument(
                    "depth must be greater than or equal to 0",
                    "Use a non-negative depth or omit the parameter to use the default semantic summary depth.");
            }

            if (!SceneTraversalDepthModes.TryParse(depthMode, out var traversalDepthMode))
            {
                return ToolErrorFactory.InvalidArgument(
                    "depthMode must be 'visual' or 'semantic'",
                    "Use depthMode='semantic' to skip layout-only wrappers, or omit the parameter to keep the default semantic depth semantics.");
            }

            var root = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (root == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var maxDepth = depth ?? 3;
            var rootElementId = _elementFinder.GenerateElementId(root);
            var rootType = root.GetType().Name;
            var budget = new UiSummaryBudget();
            var rootName = TruncateString(SceneSummaryElementHelpers.GetElementName(root), budget);
            var (scopeVisibility, isCurrentlyVisible) = SceneSummaryElementHelpers.GetScopeVisibilityMetadata(root);
            var nodes = summaryOnly ? null : new List<object>();
            var navigationNodes = summaryOnly ? new List<object>() : null;
            var summary = new StringBuilder();
            var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
            var semanticNodeCount = 0;

            Traverse(
                root,
                currentDepth: 0,
                maxDepth,
                traversalDepthMode,
                nodes,
                navigationNodes,
                summary,
                visited,
                budget,
                ref semanticNodeCount);

            return summaryOnly
                ? new
                {
                    success = true,
                    rootElementId,
                    rootElementType = rootType,
                    rootElementName = rootName,
                    depth = maxDepth,
                    depthMode = SceneTraversalDepthModes.ToContractValue(traversalDepthMode),
                    scopeVisibility,
                    isCurrentlyVisible,
                    semanticNodeCount,
                    traversalNodeCount = budget.TraversalNodeCount,
                    omittedNodeCount = budget.OmittedNodeCount,
                    omittedSemanticNodeCount = budget.OmittedSemanticNodeCount,
                    truncated = budget.Truncated,
                    truncationReasons = budget.TruncationReasons,
                    payloadLimits = CreatePayloadLimits(),
                    summaryText = summary.ToString().TrimEnd(),
                    navigationNodes = navigationNodes ?? []
                }
                : new
                {
                    success = true,
                    rootElementId,
                    rootElementType = rootType,
                    rootElementName = rootName,
                    depth = maxDepth,
                    depthMode = SceneTraversalDepthModes.ToContractValue(traversalDepthMode),
                    scopeVisibility,
                    isCurrentlyVisible,
                    semanticNodeCount,
                    traversalNodeCount = budget.TraversalNodeCount,
                    omittedNodeCount = budget.OmittedNodeCount,
                    omittedSemanticNodeCount = budget.OmittedSemanticNodeCount,
                    truncated = budget.Truncated,
                    truncationReasons = budget.TruncationReasons,
                    payloadLimits = CreatePayloadLimits(),
                    summaryText = summary.ToString().TrimEnd(),
                    nodes = nodes ?? []
                };
        });
    }

    private void Traverse(
        DependencyObject current,
        int currentDepth,
        int maxDepth,
        SceneTraversalDepthMode depthMode,
        List<object>? nodes,
        List<object>? navigationNodes,
        StringBuilder summary,
        HashSet<DependencyObject> visited,
        UiSummaryBudget budget,
        ref int semanticNodeCount)
    {
        if (currentDepth > maxDepth || !visited.Add(current) || !budget.TryTakeTraversalNode())
        {
            return;
        }

        if (SceneSummaryElementHelpers.IsSemanticElement(current)
            && (currentDepth > 0 || current is FrameworkElement))
        {
            if (!AppendSemanticNode(current, currentDepth, nodes, navigationNodes, summary, budget, ref semanticNodeCount))
            {
                return;
            }
        }

        var children = SceneSummaryElementHelpers.GetSceneChildren(current);
        for (var index = 0; index < children.Count; index++)
        {
            if (budget.ShouldStopTraversal(semanticNodeCount, out var reason))
            {
                budget.OmitRemainingNodes(children.Count - index, reason);
                break;
            }

            var child = children[index];
            var nextDepth = SceneSummaryElementHelpers.GetNextTraversalDepth(child, currentDepth, depthMode);
            Traverse(
                child,
                nextDepth,
                maxDepth,
                depthMode,
                nodes,
                navigationNodes,
                summary,
                visited,
                budget,
                ref semanticNodeCount);
        }
    }

    private bool AppendSemanticNode(
        DependencyObject element,
        int depth,
        List<object>? nodes,
        List<object>? navigationNodes,
        StringBuilder summary,
        UiSummaryBudget budget,
        ref int semanticNodeCount)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return true;
        }

        var elementType = frameworkElement.GetType().Name;
        var elementName = TruncateString(SceneSummaryElementHelpers.GetElementName(frameworkElement), budget);
        var text = TruncateString(SceneSummaryElementHelpers.GetDisplayText(frameworkElement), budget);
        var annotations = SceneSummaryElementHelpers.GetAnnotations(frameworkElement);
        var kind = SceneSummaryElementHelpers.GetKind(frameworkElement);
        var currentValue = TruncatePayloadValue(SceneSummaryElementHelpers.GetCurrentValue(frameworkElement), budget);

        if (SceneSummaryElementHelpers.ShouldOmitSemanticNode(frameworkElement, kind, text, currentValue, annotations))
        {
            return true;
        }

        if (!budget.TryTakeSemanticNode(semanticNodeCount))
        {
            return false;
        }

        semanticNodeCount++;
        var elementId = _elementFinder.GenerateElementId(frameworkElement);

        if (nodes != null)
        {
            nodes.Add(new
            {
                elementId,
                elementType,
                elementName,
                kind,
                depth,
                text,
                currentValue,
                annotations
            });
        }

        if (navigationNodes != null && annotations.Count > 0)
        {
            navigationNodes.Add(new
            {
                elementId,
                elementType,
                annotations
            });
        }

        var indent = new string(' ', depth * 2);
        var nameSegment = string.IsNullOrWhiteSpace(elementName) ? string.Empty : $" {elementName}";
        var textSegment = string.IsNullOrWhiteSpace(text) ? string.Empty : $" \"{text}\"";
        var annotationSegment = annotations.Count == 0 ? string.Empty : $" [{string.Join(", ", annotations)}]";
        AppendBoundedSummaryLine(
            summary,
            $"{indent}- {elementType}{nameSegment}{textSegment}{annotationSegment}",
            budget);

        return true;
    }

    private static object CreatePayloadLimits() => new
    {
        maxTraversalNodes = MaxTraversalNodes,
        maxSemanticNodes = MaxSemanticNodes,
        maxSummaryTextLength = MaxSummaryTextLength,
        maxStringValueLength = MaxStringValueLength
    };

    private static object? TruncatePayloadValue(object? value, UiSummaryBudget budget)
    {
        return value is string text
            ? TruncateString(text, budget)
            : value;
    }

    private static string? TruncateString(string? value, UiSummaryBudget budget)
    {
        if (value == null || value.Length <= MaxStringValueLength)
        {
            return value;
        }

        budget.MarkTruncated("StringValueLength");
        return value[..(MaxStringValueLength - 3)] + "...";
    }

    private static void AppendBoundedSummaryLine(StringBuilder summary, string line, UiSummaryBudget budget)
    {
        var value = line + Environment.NewLine;
        var remaining = MaxSummaryTextLength - summary.Length;
        if (remaining <= 0)
        {
            budget.MarkTruncated("SummaryTextLength");
            return;
        }

        if (value.Length > remaining)
        {
            summary.Append(value[..remaining]);
            budget.MarkTruncated("SummaryTextLength");
            return;
        }

        summary.Append(value);
    }

    private sealed class UiSummaryBudget
    {
        private readonly List<string> _truncationReasons = [];

        public int TraversalNodeCount { get; private set; }

        public int OmittedNodeCount { get; private set; }

        public int OmittedSemanticNodeCount { get; private set; }

        public bool Truncated => _truncationReasons.Count > 0;

        public IReadOnlyList<string> TruncationReasons => _truncationReasons;

        public bool TryTakeTraversalNode()
        {
            if (TraversalNodeCount >= MaxTraversalNodes)
            {
                MarkTruncated("TraversalNodeLimit");
                return false;
            }

            TraversalNodeCount++;
            return true;
        }

        public bool TryTakeSemanticNode(int semanticNodeCount)
        {
            if (semanticNodeCount >= MaxSemanticNodes)
            {
                OmittedSemanticNodeCount++;
                MarkTruncated("SemanticNodeLimit");
                return false;
            }

            return true;
        }

        public bool ShouldStopTraversal(int semanticNodeCount, out string reason)
        {
            if (TraversalNodeCount >= MaxTraversalNodes)
            {
                reason = "TraversalNodeLimit";
                return true;
            }

            if (semanticNodeCount >= MaxSemanticNodes)
            {
                reason = "SemanticNodeLimit";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        public void OmitRemainingNodes(int count, string reason)
        {
            if (count <= 0)
            {
                return;
            }

            OmittedNodeCount += count;
            MarkTruncated(reason);
        }

        public void MarkTruncated(string reason)
        {
            if (!_truncationReasons.Contains(reason, StringComparer.Ordinal))
            {
                _truncationReasons.Add(reason);
            }
        }

    }
}
