using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class MvvmAnalyzer
{
    private const int MaxValidationErrors = 200;
    private const int MaxValidationTraversalNodes = 512;

    private (int TraversalNodeCount, bool TraversalTruncated, bool ValidationErrorsTruncated) CollectValidationErrors(
        DependencyObject element, List<object> errors, int maxDepth)
    {
        var traversal = DependencyObjectTraversal.EnumerateDescendantsAndSelfWithMetadata(
            element,
            maxDepth,
            MaxValidationTraversalNodes);
        var traversalNodeCount = 0;
        foreach (var current in traversal)
        {
            traversalNodeCount++;
            if (errors.Count >= MaxValidationErrors)
            {
                return (traversalNodeCount, false, true);
            }

            foreach (var error in Validation.GetErrors(current))
            {
                if (errors.Count >= MaxValidationErrors)
                {
                    return (traversalNodeCount, false, true);
                }

                var elementName = (current as FrameworkElement)?.Name;
                var elementType = current.GetType().Name;
                errors.Add(new Dictionary<string, object?>
                {
                    ["diagnosticKind"] = "ValidationError",
                    ["sourceKind"] = error.RuleInError != null ? "ValidationRule" : "BindingValidation",
                    ["errorContent"] = error.ErrorContent?.ToString(),
                    ["isRuleError"] = error.RuleInError != null,
                    ["ruleType"] = error.RuleInError?.GetType().Name,
                    ["elementType"] = elementType,
                    ["elementName"] = string.IsNullOrEmpty(elementName) ? null : elementName,
                    ["elementId"] = _elementFinder.GenerateElementId(current)
                });
            }
        }

        return (traversalNodeCount, traversal.Truncated, false);
    }
}
