using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Searches the live WPF visual/logical tree for elements that match AI-friendly exact filters.
/// </summary>
public sealed partial class ElementSearchAnalyzer : DispatcherAnalyzerBase
{
    internal const int MaxSearchPropertyNameLength = 256;
    internal const int MaxSearchQueryLength = 256;
    internal const int MaxDependencyPropertyCacheEntries = 512;

    private readonly ElementFinder _elementFinder;
    private readonly Func<DependencyObject, string, DependencyProperty?> _dependencyPropertyResolver;
    private readonly ConcurrentDictionary<(Type ElementType, string PropertyName), DependencyPropertyLookupResult> _dependencyPropertyCache = new();
    private readonly ConcurrentQueue<(Type ElementType, string PropertyName)> _dependencyPropertyCacheInsertionOrder = new();

    /// <summary>
    /// Initializes a new analyzer using the shared element finder/cache.
    /// </summary>
    public ElementSearchAnalyzer(ElementFinder elementFinder)
        : this(elementFinder, static (element, propertyName) => FindDependencyProperty(element, propertyName))
    {
    }

    internal ElementSearchAnalyzer(
        ElementFinder elementFinder,
        Func<DependencyObject, string, DependencyProperty?> dependencyPropertyResolver)
    {
        _elementFinder = elementFinder;
        _dependencyPropertyResolver = dependencyPropertyResolver ?? throw new ArgumentNullException(nameof(dependencyPropertyResolver));
    }

    internal int DependencyPropertyCacheEntryCount => _dependencyPropertyCache.Count;

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
        string? matchMode = null,
        string? query = null,
        string? typeMatchMode = null)
    {
        return FindElementsCore(
            rootElementId,
            query,
            typeName,
            typeNames,
            elementName,
            automationId,
            propertyName,
            propertyValue,
            maxResults,
            maxTraversalNodes: null,
            matchMode,
            typeMatchMode);
    }

    internal object FindElementsWithTraversalBudget(
        string? rootElementId = null,
        string? typeName = null,
        string[]? typeNames = null,
        string? elementName = null,
        string? automationId = null,
        string? propertyName = null,
        string? propertyValue = null,
        int? maxResults = null,
        int? maxTraversalNodes = null,
        string? matchMode = null,
        string? query = null,
        string? typeMatchMode = null)
    {
        return FindElementsCore(
            rootElementId,
            query,
            typeName,
            typeNames,
            elementName,
            automationId,
            propertyName,
            propertyValue,
            maxResults,
            maxTraversalNodes,
            matchMode,
            typeMatchMode);
    }

    private object FindElementsCore(
        string? rootElementId,
        string? query,
        string? typeName,
        string[]? typeNames,
        string? elementName,
        string? automationId,
        string? propertyName,
        string? propertyValue,
        int? maxResults,
        int? maxTraversalNodes,
        string? matchMode,
        string? typeMatchMode)
    {
        var limit = maxResults.GetValueOrDefault(20);
        if (limit <= 0)
        {
            return ToolErrorFactory.InvalidArgument("maxResults must be a positive integer.");
        }

        if (maxTraversalNodes is <= 0)
        {
            return ToolErrorFactory.InvalidArgument("maxTraversalNodes must be a positive integer.");
        }

        var traversalLimit = ResolveTraversalLimit(maxTraversalNodes);
        var rootLookupLimit = maxTraversalNodes.HasValue ? traversalLimit : (int?)null;
        var root = ResolveSearchRoot(rootElementId, rootLookupLimit);

        return InvokeOnDispatcher<object>(root?.Dispatcher ?? Application.Current?.Dispatcher, () =>
        {
            var resolvedRoot = root ?? ResolveSearchRoot(rootElementId, rootLookupLimit);

            if (resolvedRoot == null)
            {
                return ToolErrorFactory.ElementNotFound(rootElementId);
            }

            if (propertyName?.Length > MaxSearchPropertyNameLength)
            {
                return ToolErrorFactory.InvalidArgument($"propertyName must be {MaxSearchPropertyNameLength} characters or fewer.");
            }

            if (query?.Length > MaxSearchQueryLength)
            {
                return ToolErrorFactory.InvalidArgument($"query must be {MaxSearchQueryLength} characters or fewer.");
            }

            if (!string.IsNullOrWhiteSpace(typeName) && typeNames is { Length: > 0 })
            {
                return ToolErrorFactory.InvalidArgument(
                    "Provide either typeName or typeNames, not both.",
                    "Use typeName for a single exact type or typeNames for an OR-style multi-type search.");
            }

            var resolvedMatchMode = string.IsNullOrWhiteSpace(matchMode) ? "exact" : matchMode!;
            var resolvedQueryMatchMode = string.IsNullOrWhiteSpace(matchMode) ? "contains" : resolvedMatchMode;
            if (!string.Equals(resolvedMatchMode, "exact", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(resolvedMatchMode, "contains", StringComparison.OrdinalIgnoreCase))
            {
                return ToolErrorFactory.InvalidArgument(
                    $"Unsupported matchMode '{matchMode}'.",
                    "Use matchMode 'exact' or 'contains'.");
            }

            var resolvedTypeMatchMode = string.IsNullOrWhiteSpace(typeMatchMode) ? "exact" : typeMatchMode!;
            if (!string.Equals(resolvedTypeMatchMode, "exact", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(resolvedTypeMatchMode, "assignable", StringComparison.OrdinalIgnoreCase))
            {
                return ToolErrorFactory.InvalidArgument(
                    $"Unsupported typeMatchMode '{typeMatchMode}'.",
                    "Use typeMatchMode 'exact' or 'assignable'.");
            }

            var results = new List<object>();
            var truncated = false;
            var traversalNodeCount = 0;
            var traversalTruncated = false;
            string? truncationReason = null;

            var traversal = DependencyObjectTraversal.EnumerateDescendantsAndSelfWithMetadata(resolvedRoot, maxNodes: traversalLimit);
            foreach (var current in traversal)
            {
                traversalNodeCount++;

                if (!Matches(
                        current,
                        query,
                        typeName,
                        typeNames,
                        elementName,
                        automationId,
                        propertyName,
                        propertyValue,
                        resolvedMatchMode,
                        resolvedTypeMatchMode,
                        resolvedQueryMatchMode,
                        out var matchedProperty,
                        out var matchedValue))
                {
                    continue;
                }

                results.Add(new
                {
                    elementId = _elementFinder.GenerateElementId(current),
                    elementType = current.GetType().Name,
                    elementName = (current as FrameworkElement)?.Name,
                    automationId = current is DependencyObject depObj ? AutomationProperties.GetAutomationId(depObj) : null,
                    matchedProperty,
                    matchedValue
                });

                if (results.Count >= limit)
                {
                    truncated = true;
                    truncationReason = "maxResults";
                    break;
                }
            }

            if (!truncated && traversal.Truncated)
            {
                truncated = true;
                traversalTruncated = true;
                truncationReason = "maxTraversalNodes";
            }

            return new
            {
                success = true,
                resultCount = results.Count,
                truncated,
                traversalNodeCount,
                maxTraversalNodes = traversalLimit,
                traversalTruncated,
                truncationReason,
                results
            };
        });
    }

    private bool Matches(
        DependencyObject element,
        string? query,
        string? typeName,
        string[]? typeNames,
        string? elementName,
        string? automationId,
        string? propertyName,
        string? propertyValue,
        string matchMode,
        string typeMatchMode,
        string queryMatchMode,
        out string? matchedProperty,
        out string? matchedValue)
    {
        matchedProperty = null;
        matchedValue = null;

        if (!MatchesType(element.GetType(), typeName, typeNames, matchMode, typeMatchMode))
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

        var requestedPropertyName = propertyName;
        if (!string.IsNullOrWhiteSpace(requestedPropertyName))
        {
            var value = TryGetPropertyValue(element, requestedPropertyName!);
            matchedProperty = requestedPropertyName;
            matchedValue = FormatResponseValue(value);

            if (propertyValue == null)
            {
                if (value == null)
                {
                    return false;
                }
            }
            else if (!MatchesValue(matchedValue, propertyValue, matchMode))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (!TryMatchQuery(element, query!, queryMatchMode, out var queryMatchedProperty, out var queryMatchedValue))
        {
            return false;
        }

        matchedProperty ??= queryMatchedProperty;
        matchedValue ??= queryMatchedValue;
        return true;
    }

    private static bool TryMatchQuery(
        DependencyObject element,
        string query,
        string queryMatchMode,
        out string? matchedProperty,
        out string? matchedValue)
    {
        foreach (var candidate in GetSemanticQueryCandidates(element))
        {
            if (!MatchesValue(candidate.Value, query, queryMatchMode))
            {
                continue;
            }

            matchedProperty = candidate.PropertyName;
            matchedValue = candidate.Value;
            return true;
        }

        matchedProperty = null;
        matchedValue = null;
        return false;
    }

    private static IEnumerable<(string PropertyName, string? Value)> GetSemanticQueryCandidates(DependencyObject element)
    {
        yield return ("elementType", element.GetType().Name);

        if (element is FrameworkElement frameworkElement)
        {
            yield return ("elementName", frameworkElement.Name);
        }

        yield return ("automationId", AutomationProperties.GetAutomationId(element));

        if (element is TextBlock textBlock)
        {
            yield return ("Text", textBlock.Text);
        }
        else if (element is TextBox textBox)
        {
            yield return ("Text", textBox.Text);
        }
        else if (element is ComboBox comboBox)
        {
            yield return ("Text", comboBox.Text);
        }

        if (element is ContentControl contentControl)
        {
            yield return ("Content", FormatResponseValue(contentControl.Content));
        }

        if (element is HeaderedContentControl headeredContentControl)
        {
            yield return ("Header", FormatResponseValue(headeredContentControl.Header));
        }

        if (element is HeaderedItemsControl headeredItemsControl)
        {
            yield return ("Header", FormatResponseValue(headeredItemsControl.Header));
        }
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

        var actualValue = actual;
        if (string.IsNullOrWhiteSpace(actualValue))
        {
            return false;
        }

        return string.Equals(matchMode, "contains", StringComparison.OrdinalIgnoreCase)
            ? actualValue!.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0
            : string.Equals(actualValue, expected, StringComparison.Ordinal);
    }

    private object? TryGetPropertyValue(DependencyObject element, string propertyName)
    {
        var dependencyProperty = FindCachedDependencyProperty(element, propertyName);
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

    private DependencyProperty? FindCachedDependencyProperty(DependencyObject element, string propertyName)
    {
        var key = (element.GetType(), propertyName);
        if (_dependencyPropertyCache.TryGetValue(key, out var cachedResult))
        {
            return cachedResult.Property;
        }

        var result = new DependencyPropertyLookupResult(_dependencyPropertyResolver(element, propertyName));
        if (_dependencyPropertyCache.TryAdd(key, result))
        {
            _dependencyPropertyCacheInsertionOrder.Enqueue(key);
            TrimDependencyPropertyCache();
        }

        return result.Property;
    }

    private void TrimDependencyPropertyCache()
    {
        while (_dependencyPropertyCache.Count > MaxDependencyPropertyCacheEntries &&
               _dependencyPropertyCacheInsertionOrder.TryDequeue(out var oldestKey))
        {
            _dependencyPropertyCache.TryRemove(oldestKey, out _);
        }
    }

    private static int ResolveTraversalLimit(int? maxTraversalNodes)
    {
        var resolved = maxTraversalNodes ?? TreeTraversalDefaults.DefaultMaxNodes;
        return Math.Max(1, Math.Min(resolved, TreeTraversalDefaults.MaxNodesLimit));
    }

    private DependencyObject? ResolveSearchRoot(string? rootElementId, int? maxTraversalNodes)
    {
        return rootElementId == null
            ? ResolveRootElement()
            : _elementFinder.FindById(rootElementId, maxTraversalNodes: maxTraversalNodes);
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

    private readonly struct DependencyPropertyLookupResult
    {
        public DependencyPropertyLookupResult(DependencyProperty? property)
        {
            Property = property;
        }

        public DependencyProperty? Property { get; }
    }
}
