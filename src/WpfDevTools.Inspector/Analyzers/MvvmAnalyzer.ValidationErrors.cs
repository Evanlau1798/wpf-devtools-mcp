using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class MvvmAnalyzer
{
    private const int MaxValidationErrors = 200;

    private void CollectValidationErrors(
        DependencyObject element, List<object> errors, int maxDepth)
    {
        foreach (var current in DependencyObjectTraversal.EnumerateDescendantsAndSelf(element, maxDepth))
        {
            if (errors.Count >= MaxValidationErrors)
            {
                return;
            }

            foreach (var error in Validation.GetErrors(current))
            {
                if (errors.Count >= MaxValidationErrors)
                {
                    return;
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
    }
}
