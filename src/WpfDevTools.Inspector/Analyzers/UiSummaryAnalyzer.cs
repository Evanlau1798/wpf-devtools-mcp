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
            var rootName = SceneSummaryElementHelpers.GetElementName(root);
            var (scopeVisibility, isCurrentlyVisible) = SceneSummaryElementHelpers.GetScopeVisibilityMetadata(root);
            var nodes = summaryOnly ? null : new List<object>();
            var summary = new StringBuilder();
            var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
            var semanticNodeCount = 0;

            Traverse(root, currentDepth: 0, maxDepth, traversalDepthMode, nodes, summary, visited, ref semanticNodeCount);

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
                    summaryText = summary.ToString().TrimEnd()
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
        StringBuilder summary,
        HashSet<DependencyObject> visited,
        ref int semanticNodeCount)
    {
        if (currentDepth > maxDepth || !visited.Add(current))
        {
            return;
        }

        if (SceneSummaryElementHelpers.IsSemanticElement(current)
            && (currentDepth > 0 || current is FrameworkElement))
        {
            AppendSemanticNode(current, currentDepth, nodes, summary, ref semanticNodeCount);
        }

        foreach (var child in SceneSummaryElementHelpers.GetSceneChildren(current))
        {
            var nextDepth = SceneSummaryElementHelpers.GetNextTraversalDepth(child, currentDepth, depthMode);
            Traverse(child, nextDepth, maxDepth, depthMode, nodes, summary, visited, ref semanticNodeCount);
        }
    }

    private void AppendSemanticNode(
        DependencyObject element,
        int depth,
        List<object>? nodes,
        StringBuilder summary,
        ref int semanticNodeCount)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return;
        }

        var elementType = frameworkElement.GetType().Name;
        var elementName = SceneSummaryElementHelpers.GetElementName(frameworkElement);
        var text = SceneSummaryElementHelpers.GetDisplayText(frameworkElement);
        var annotations = SceneSummaryElementHelpers.GetAnnotations(frameworkElement);
        var kind = SceneSummaryElementHelpers.GetKind(frameworkElement);
        var currentValue = SceneSummaryElementHelpers.GetCurrentValue(frameworkElement);

        if (SceneSummaryElementHelpers.ShouldOmitSemanticNode(frameworkElement, kind, text, currentValue, annotations))
        {
            return;
        }

        semanticNodeCount++;

        if (nodes != null)
        {
            nodes.Add(new
            {
                elementId = _elementFinder.GenerateElementId(frameworkElement),
                elementType,
                elementName,
                kind,
                depth,
                text,
                currentValue,
                annotations
            });
        }

        var indent = new string(' ', depth * 2);
        var nameSegment = string.IsNullOrWhiteSpace(elementName) ? string.Empty : $" {elementName}";
        var textSegment = string.IsNullOrWhiteSpace(text) ? string.Empty : $" \"{text}\"";
        var annotationSegment = annotations.Count == 0 ? string.Empty : $" [{string.Join(", ", annotations)}]";
        summary.AppendLine($"{indent}- {elementType}{nameSegment}{textSegment}{annotationSegment}");
    }
}
