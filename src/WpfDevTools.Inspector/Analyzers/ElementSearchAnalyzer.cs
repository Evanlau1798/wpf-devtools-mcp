using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Searches the live WPF visual/logical tree for elements that match AI-friendly exact filters.
/// </summary>
public sealed class ElementSearchAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    /// <summary>
    /// Initializes a new analyzer using the shared element finder/cache.
    /// </summary>
    public ElementSearchAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Finds matching elements under the root window or the specified root element.
    /// </summary>
    public object FindElements(
        string? rootElementId = null,
        string? typeName = null,
        string[]? typeNames = null,
        string? elementName = null,
        string? automationId = null,
        string? propertyName = null,
        string? propertyValue = null,
        int? maxResults = null,
        string? matchMode = null)
    {
        var root = rootElementId == null
            ? ResolveRootElement()
            : _elementFinder.FindById(rootElementId);

        return InvokeOnDispatcher<object>(root?.Dispatcher ?? Application.Current?.Dispatcher, () =>
        {
            var resolvedRoot = root ?? (rootElementId == null
                ? ResolveRootElement()
                : _elementFinder.FindById(rootElementId));

            if (resolvedRoot == null)
            {
                return ToolErrorFactory.ElementNotFound(rootElementId);
            }

            var limit = maxResults.GetValueOrDefault(20);
            if (limit <= 0)
            {
                return ToolErrorFactory.InvalidArgument("maxResults must be a positive integer.");
            }

            if (!string.IsNullOrWhiteSpace(typeName) && typeNames is { Length: > 0 })
            {
                return ToolErrorFactory.InvalidArgument(
                    "Provide either typeName or typeNames, not both.",
                    "Use typeName for a single exact type or typeNames for an OR-style multi-type search.");
            }

            var resolvedMatchMode = string.IsNullOrWhiteSpace(matchMode) ? "exact" : matchMode!;
            if (!string.Equals(resolvedMatchMode, "exact", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(resolvedMatchMode, "contains", StringComparison.OrdinalIgnoreCase))
            {
                return ToolErrorFactory.InvalidArgument(
                    $"Unsupported matchMode '{matchMode}'.",
                    "Use matchMode 'exact' or 'contains'.");
            }

            var results = new List<object>();
            var truncated = false;

            foreach (var current in DependencyObjectTraversal.EnumerateDescendantsAndSelf(resolvedRoot))
            {
                if (!Matches(current, typeName, typeNames, elementName, automationId, propertyName, propertyValue, resolvedMatchMode, out var matchedValue))
                {
                    continue;
                }

                results.Add(new
                {
                    elementId = _elementFinder.GenerateElementId(current),
                    elementType = current.GetType().Name,
                    elementName = (current as FrameworkElement)?.Name,
                    automationId = current is DependencyObject depObj ? AutomationProperties.GetAutomationId(depObj) : null,
                    matchedProperty = propertyName,
                    matchedValue
                });

                if (results.Count >= limit)
                {
                    truncated = true;
                    break;
                }
            }

            return new
            {
                success = true,
                resultCount = results.Count,
                truncated,
                results
            };
        });
    }

    private static bool Matches(
        DependencyObject element,
        string? typeName,
        string[]? typeNames,
        string? elementName,
        string? automationId,
        string? propertyName,
        string? propertyValue,
        string matchMode,
        out string? matchedValue)
    {
        matchedValue = null;

        if (!MatchesString(element.GetType().Name, typeName, typeNames, matchMode))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(elementName) &&
            !MatchesValue((element as FrameworkElement)?.Name, elementName, matchMode))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(automationId) &&
            !MatchesValue(AutomationProperties.GetAutomationId(element), automationId, matchMode))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return true;
        }

        var value = TryGetPropertyValue(element, propertyName);
        matchedValue = FormatResponseValue(value);

        if (propertyValue == null)
        {
            return value != null;
        }

        return MatchesValue(matchedValue, propertyValue, matchMode);
    }

    private static bool MatchesString(string actual, string? exactValue, string[]? alternatives, string matchMode)
    {
        if (!string.IsNullOrWhiteSpace(exactValue))
        {
            return MatchesValue(actual, exactValue, matchMode);
        }

        if (alternatives is not { Length: > 0 })
        {
            return true;
        }

        return alternatives.Any(candidate => MatchesValue(actual, candidate, matchMode));
    }

    private static bool MatchesValue(string? actual, string? expected, string matchMode)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        return string.Equals(matchMode, "contains", StringComparison.OrdinalIgnoreCase)
            ? actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
            : string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static object? TryGetPropertyValue(DependencyObject element, string propertyName)
    {
        var dependencyProperty = FindDependencyProperty(element, propertyName);
        if (dependencyProperty != null)
        {
            return element.GetValue(dependencyProperty);
        }

        var clrProperty = element.GetType().GetProperty(propertyName);
        if (clrProperty != null)
        {
            return clrProperty.GetValue(element);
        }

        if (element is ContentControl contentControl && string.Equals(propertyName, "Content", StringComparison.Ordinal))
        {
            return contentControl.Content;
        }

        return null;
    }

    private DependencyObject? ResolveRootElement()
    {
        var root = _elementFinder.GetRootElement();
        if (root != null)
        {
            return root;
        }

        var application = Application.Current;
        if (application?.Windows.Count > 0)
        {
            return application.Windows[0];
        }

        return null;
    }
}
