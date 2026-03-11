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

    private List<object> CollectBindingsRecursive(DependencyObject element)
    {
        var bindings = new List<object>();
        var visited = new HashSet<DependencyObject>();
        CollectBindingsRecursiveCore(element, visited, bindings);
        return bindings;
    }

    private void CollectBindingsRecursiveCore(
        DependencyObject element,
        HashSet<DependencyObject> visited,
        List<object> bindings)
    {
        if (!visited.Add(element))
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

            bindings.AddRange(elementBindings);
        }

        foreach (var child in DependencyObjectTraversal.EnumerateChildren(element))
        {
            CollectBindingsRecursiveCore(child, visited, bindings);
        }
    }

    private static List<object> GetDependencyPropertiesWithBindings(DependencyObject element)
    {
        var bindings = new List<object>();
        var seenProperties = new HashSet<string>();

        var enumerator = element.GetLocalValueEnumerator();
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            var dp = entry.Property;

            if (dp != null && seenProperties.Add(dp.Name))
            {
                var bindingExpr = BindingOperations.GetBindingExpression(element, dp);
                if (bindingExpr != null)
                {
                    var binding = bindingExpr.ParentBinding;

                    bindings.Add(new Dictionary<string, object?>
                    {
                        ["propertyName"] = dp.Name,
                        ["path"] = binding?.Path?.Path,
                        ["mode"] = binding?.Mode.ToString(),
                        ["updateSourceTrigger"] = binding?.UpdateSourceTrigger.ToString(),
                        ["status"] = bindingExpr.Status.ToString()
                    });
                }
            }
        }

        return bindings;
    }

    private static object? ApplyStatusFilter(List<object> bindings, string? statusFilter)
    {
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

        var filtered = bindings
            .Where(binding => binding is Dictionary<string, object?> dict &&
                              predicate(dict.TryGetValue("status", out var status) ? status?.ToString() : null))
            .ToList();

        return new { success = true, bindings = filtered };
    }
}
