using System.Windows;
using System.Windows.Data;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private DependencyObject? ResolveElement(string? elementId)
    {
        return elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);
    }

    private List<object> CollectBindingsForElement(
        DependencyObject element,
        BindingScanBudget budget)
    {
        var bindings = new List<object>();
        if (!budget.TryTakeTraversalNode())
        {
            return bindings;
        }

        foreach (var binding in GetDependencyPropertiesWithBindings(element))
        {
            if (budget.TryTakeResult())
            {
                bindings.Add(binding);
            }
        }

        return bindings;
    }

    private List<object> CollectBindingsRecursive(
        DependencyObject element,
        BindingScanBudget budget)
    {
        var bindings = new List<object>();
        var visited = new HashSet<DependencyObject>();
        CollectBindingsRecursiveCore(element, visited, bindings, budget);
        return bindings;
    }

    private void CollectBindingsRecursiveCore(
        DependencyObject element,
        HashSet<DependencyObject> visited,
        List<object> bindings,
        BindingScanBudget budget)
    {
        if (!visited.Add(element))
        {
            return;
        }

        if (!budget.TryTakeTraversalNode())
        {
            return;
        }

        var elementBindings = GetDependencyPropertiesWithBindings(element);
        if (elementBindings.Count > 0)
        {
            var elementId = _elementFinder.GenerateElementId(element);
            var elementType = element.GetType().Name;

            foreach (var binding in elementBindings)
            {
                if (binding is Dictionary<string, object?> dict)
                {
                    dict["elementId"] = elementId;
                    dict["elementType"] = elementType;
                }
            }

            foreach (var binding in elementBindings)
            {
                if (budget.TryTakeResult())
                {
                    bindings.Add(binding);
                }
            }
        }

        foreach (var child in DependencyObjectTraversal.EnumerateChildren(element))
        {
            CollectBindingsRecursiveCore(child, visited, bindings, budget);
        }
    }

    private static List<object> GetDependencyPropertiesWithBindings(DependencyObject element)
    {
        var bindings = new List<object>();
        foreach (var dp in GetCandidateDependencyProperties(element))
        {
            var bindingExpr = BindingOperations.GetBindingExpressionBase(element, dp);
            if (bindingExpr != null)
            {
                bindings.Add(BuildBindingPayload(element, dp, bindingExpr));
            }
        }

        return bindings;
    }

    private static Dictionary<string, object?> BuildBindingPayload(
        DependencyObject element,
        DependencyProperty dp,
        BindingExpressionBase bindingExpression)
    {
        return bindingExpression switch
        {
            BindingExpression simpleBinding => BuildSimpleBindingPayload(element, dp, simpleBinding),
            MultiBindingExpression multiBinding => BuildMultiBindingPayload(element, dp, multiBinding),
            _ => BuildFallbackBindingPayload(element, dp, bindingExpression)
        };
    }

    private static Dictionary<string, object?> BuildSimpleBindingPayload(
        DependencyObject element,
        DependencyProperty dp,
        BindingExpression bindingExpression)
    {
        var binding = bindingExpression.ParentBinding;

        return new Dictionary<string, object?>
        {
            ["propertyName"] = dp.Name,
            ["bindingType"] = "Binding",
            ["path"] = binding?.Path?.Path,
            ["mode"] = binding?.Mode.ToString(),
            ["updateSourceTrigger"] = binding?.UpdateSourceTrigger.ToString(),
            ["status"] = bindingExpression.Status.ToString(),
            ["converter"] = binding?.Converter?.GetType().Name,
            ["currentValue"] = FormatResponseValue(element.GetValue(dp))
        };
    }

    private static Dictionary<string, object?> BuildMultiBindingPayload(
        DependencyObject element,
        DependencyProperty dp,
        MultiBindingExpression bindingExpression)
    {
        var binding = bindingExpression.ParentMultiBinding;
        var bindingPaths = binding?.Bindings
            .OfType<Binding>()
            .Select(child => child.Path?.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray() ?? Array.Empty<string>();

        return new Dictionary<string, object?>
        {
            ["propertyName"] = dp.Name,
            ["bindingType"] = "MultiBinding",
            ["path"] = bindingPaths.Length > 0 ? string.Join(", ", bindingPaths) : null,
            ["bindingPaths"] = bindingPaths,
            ["mode"] = binding?.Mode.ToString(),
            ["updateSourceTrigger"] = binding?.UpdateSourceTrigger.ToString(),
            ["status"] = bindingExpression.Status.ToString(),
            ["converter"] = binding?.Converter?.GetType().Name,
            ["currentValue"] = FormatResponseValue(element.GetValue(dp))
        };
    }

    private static Dictionary<string, object?> BuildFallbackBindingPayload(
        DependencyObject element,
        DependencyProperty dp,
        BindingExpressionBase bindingExpression)
    {
        return new Dictionary<string, object?>
        {
            ["propertyName"] = dp.Name,
            ["bindingType"] = bindingExpression.GetType().Name,
            ["path"] = null,
            ["mode"] = null,
            ["updateSourceTrigger"] = null,
            ["status"] = bindingExpression.Status.ToString(),
            ["converter"] = null,
            ["currentValue"] = FormatResponseValue(element.GetValue(dp))
        };
    }

    private static object? ApplyStatusFilter(
        List<object> bindings,
        string? statusFilter,
        out List<object> filteredBindings)
    {
        filteredBindings = bindings;
        if (string.IsNullOrWhiteSpace(statusFilter) ||
            string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Func<string?, bool> predicate = statusFilter switch
        {
            var value when string.Equals(value, "Active", StringComparison.OrdinalIgnoreCase)
                => status => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase),
            var value when string.Equals(value, "Error", StringComparison.OrdinalIgnoreCase)
                => status => status is "PathError" or "UpdateTargetError" or "UpdateSourceError",
            _ => null!
        };

        if (predicate is null)
        {
            return ToolErrorFactory.InvalidArgument(
                $"Unsupported statusFilter '{statusFilter}'",
                "Use statusFilter 'All', 'Active', or 'Error'.");
        }

        filteredBindings = bindings
            .Where(binding => binding is Dictionary<string, object?> dict &&
                              predicate(dict.TryGetValue("status", out var status) ? status?.ToString() : null))
            .ToList();

        return null;
    }
}
