using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    /// <summary>
    /// Return a best-effort list of elements whose simple binding path exactly matches the supplied ViewModel property name.
    /// </summary>
    /// <param name="propertyName">ViewModel property name to match against simple binding paths.</param>
    /// <param name="viewModelType">Optional coarse DataContext type filter.</param>
    /// <param name="elementId">Optional root element ID that scopes the search.</param>
    /// <param name="recursive">When true, scan descendants under the chosen root element.</param>
    /// <returns>Result object containing best-effort candidate elements and explicit verification metadata.</returns>
    public object GetAffectedElements(
        string propertyName,
        string? viewModelType = null,
        string? elementId = null,
        bool recursive = true)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return ToolErrorFactory.InvalidArgument(
                    "propertyName is required",
                    "Provide the ViewModel property name you want to match against simple binding paths.");
            }

            var element = ResolveElement(elementId);
            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var affectedElements = new List<object>();
            if (recursive)
            {
                var visited = new HashSet<DependencyObject>();
                CollectAffectedElementsRecursive(
                    element,
                    propertyName.Trim(),
                    viewModelType,
                    visited,
                    affectedElements);
            }
            else
            {
                affectedElements.AddRange(GetAffectedElementsForSingleElement(
                    element,
                    propertyName.Trim(),
                    viewModelType));
            }

            return new
            {
                success = true,
                propertyName = propertyName.Trim(),
                viewModelType,
                confidence = "best-effort",
                matchStrategy = "simple-path-match",
                requiresVerification = true,
                affectedCount = affectedElements.Count,
                affectedElements
            };
        });
    }

    private void CollectAffectedElementsRecursive(
        DependencyObject element,
        string propertyName,
        string? viewModelType,
        HashSet<DependencyObject> visited,
        List<object> affectedElements)
    {
        if (!visited.Add(element))
        {
            return;
        }

        affectedElements.AddRange(GetAffectedElementsForSingleElement(element, propertyName, viewModelType));
        foreach (var child in DependencyObjectTraversal.EnumerateChildren(element))
        {
            CollectAffectedElementsRecursive(child, propertyName, viewModelType, visited, affectedElements);
        }
    }

    private List<object> GetAffectedElementsForSingleElement(
        DependencyObject element,
        string propertyName,
        string? viewModelType)
    {
        var results = new List<object>();
        var dataContextType = GetElementDataContextType(element);

        if (!MatchesViewModelType(dataContextType, viewModelType))
        {
            return results;
        }

        foreach (var binding in GetDependencyPropertiesWithBindings(element).OfType<Dictionary<string, object?>>())
        {
            if (!IsSimplePathMatch(binding, propertyName))
            {
                continue;
            }

            results.Add(new
            {
                elementId = _elementFinder.GenerateElementId(element),
                elementType = element.GetType().Name,
                elementName = GetElementName(element),
                dataContextType,
                propertyName = binding.TryGetValue("propertyName", out var targetProperty) ? targetProperty?.ToString() : null,
                bindingPath = binding.TryGetValue("path", out var path) ? path?.ToString() : null,
                currentValue = binding.TryGetValue("currentValue", out var currentValue) ? currentValue?.ToString() : null,
                status = binding.TryGetValue("status", out var status) ? status?.ToString() : null
            });
        }

        return results;
    }

    private static bool IsSimplePathMatch(Dictionary<string, object?> binding, string propertyName)
    {
        if (!binding.TryGetValue("bindingType", out var bindingType)
            || !string.Equals(bindingType?.ToString(), "Binding", StringComparison.Ordinal))
        {
            return false;
        }

        if (!binding.TryGetValue("path", out var pathValue))
        {
            return false;
        }

        var path = pathValue?.ToString();
        return string.Equals(path, propertyName, StringComparison.Ordinal);
    }

    private static string? GetElementDataContextType(DependencyObject element)
    {
        if (element is FrameworkElement frameworkElement)
        {
            return frameworkElement.DataContext?.GetType().Name;
        }

        if (element is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.DataContext?.GetType().Name;
        }

        return null;
    }

    private static bool MatchesViewModelType(string? actualType, string? requestedType)
    {
        if (string.IsNullOrWhiteSpace(requestedType))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(actualType))
        {
            return false;
        }

        return string.Equals(actualType, requestedType, StringComparison.Ordinal)
            || string.Equals(actualType, requestedType, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetElementName(DependencyObject element)
    {
        return element switch
        {
            FrameworkElement frameworkElement when !string.IsNullOrWhiteSpace(frameworkElement.Name) => frameworkElement.Name,
            FrameworkContentElement frameworkContentElement when !string.IsNullOrWhiteSpace(frameworkContentElement.Name) => frameworkContentElement.Name,
            _ => null
        };
    }
}
