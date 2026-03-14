using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private sealed record AffectedElementMatch(
        object Payload,
        string Confidence,
        string MatchStrategy,
        int MatchRank);

    /// <summary>
    /// Return a best-effort list of elements whose binding paths deterministically match the supplied ViewModel property name.
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

            var matches = new List<AffectedElementMatch>();
            if (recursive)
            {
                var visited = new HashSet<DependencyObject>();
                CollectAffectedElementsRecursive(
                    element,
                    propertyName.Trim(),
                    viewModelType,
                    visited,
                    matches);
            }
            else
            {
                matches.AddRange(GetAffectedElementsForSingleElement(
                    element,
                    propertyName.Trim(),
                    viewModelType));
            }

            var summary = SummarizeMatches(matches);
            return new
            {
                success = true,
                propertyName = propertyName.Trim(),
                viewModelType,
                confidence = summary.Confidence,
                matchStrategy = summary.MatchStrategy,
                requiresVerification = true,
                affectedCount = matches.Count,
                affectedElements = matches.Select(match => match.Payload).ToList()
            };
        });
    }

    private void CollectAffectedElementsRecursive(
        DependencyObject element,
        string propertyName,
        string? viewModelType,
        HashSet<DependencyObject> visited,
        List<AffectedElementMatch> affectedElements)
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

    private List<AffectedElementMatch> GetAffectedElementsForSingleElement(
        DependencyObject element,
        string propertyName,
        string? viewModelType)
    {
        var results = new List<AffectedElementMatch>();
        var dataContextType = GetElementDataContextType(element);

        if (!MatchesViewModelType(dataContextType, viewModelType))
        {
            return results;
        }

        foreach (var binding in GetDependencyPropertiesWithBindings(element).OfType<Dictionary<string, object?>>())
        {
            var match = TryMatchBinding(binding, propertyName);
            if (match == null)
            {
                continue;
            }

            results.Add(new AffectedElementMatch(
                Payload: new
                {
                    elementId = _elementFinder.GenerateElementId(element),
                    elementType = element.GetType().Name,
                    elementName = GetElementName(element),
                    dataContextType,
                    propertyName = binding.TryGetValue("propertyName", out var targetProperty) ? targetProperty?.ToString() : null,
                    bindingPath = match.BindingPath,
                    currentValue = binding.TryGetValue("currentValue", out var currentValue) ? currentValue?.ToString() : null,
                    status = binding.TryGetValue("status", out var status) ? status?.ToString() : null,
                    matchConfidence = match.Confidence
                },
                Confidence: match.Confidence,
                MatchStrategy: match.MatchStrategy,
                MatchRank: match.MatchRank));
        }

        return results;
    }

    private static (string Confidence, string MatchStrategy) SummarizeMatches(List<AffectedElementMatch> matches)
    {
        if (matches.Count == 0)
        {
            return ("best-effort", "simple-path-match");
        }

        var strongestMatch = matches
            .OrderByDescending(match => match.MatchRank)
            .First();

        return (strongestMatch.Confidence, strongestMatch.MatchStrategy);
    }

    private static BindingPathMatch? TryMatchBinding(Dictionary<string, object?> binding, string propertyName)
    {
        var bindingType = binding.TryGetValue("bindingType", out var bindingTypeValue)
            ? bindingTypeValue?.ToString()
            : null;

        if (string.Equals(bindingType, "Binding", StringComparison.Ordinal))
        {
            var path = binding.TryGetValue("path", out var pathValue)
                ? pathValue?.ToString()
                : null;

            return TryMatchSingleBindingPath(path, propertyName);
        }

        if (!string.Equals(bindingType, "MultiBinding", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var childPath in GetBindingPaths(binding))
        {
            var childPathMatch = TryClassifyPathMatch(childPath, propertyName);
            if (childPathMatch == PathMatchKind.None)
            {
                continue;
            }

            return new BindingPathMatch(
                BindingPath: childPath,
                Confidence: "high",
                MatchStrategy: "multibinding-child-path-match",
                MatchRank: 3);
        }

        return null;
    }

    private static BindingPathMatch? TryMatchSingleBindingPath(string? path, string propertyName)
    {
        return TryClassifyPathMatch(path, propertyName) switch
        {
            PathMatchKind.Exact => new BindingPathMatch(
                BindingPath: path,
                Confidence: "best-effort",
                MatchStrategy: "simple-path-match",
                MatchRank: 1),
            PathMatchKind.TerminalSegment => new BindingPathMatch(
                BindingPath: path,
                Confidence: "high",
                MatchStrategy: "terminal-path-match",
                MatchRank: 2),
            _ => null
        };
    }

    private static PathMatchKind TryClassifyPathMatch(string? path, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathMatchKind.None;
        }

        if (string.Equals(path, propertyName, StringComparison.Ordinal))
        {
            return PathMatchKind.Exact;
        }

        var terminalSegment = GetTerminalPathSegment(path);
        if (terminalSegment == null)
        {
            return PathMatchKind.None;
        }

        return string.Equals(terminalSegment, propertyName, StringComparison.Ordinal)
            ? PathMatchKind.TerminalSegment
            : PathMatchKind.None;
    }

    private static string? GetTerminalPathSegment(string path)
    {
        var segments = path
            .Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanPathSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length == 0 ? null : segments[^1];
    }

    private static string? CleanPathSegment(string segment)
    {
        var bracketIndex = segment.IndexOf('[');
        if (bracketIndex >= 0)
        {
            segment = segment[..bracketIndex];
        }

        if (segment.StartsWith('(') && segment.EndsWith(')') && segment.Length > 2)
        {
            segment = segment[1..^1];
        }

        return segment.Trim();
    }

    private static IReadOnlyList<string> GetBindingPaths(Dictionary<string, object?> binding)
    {
        if (!binding.TryGetValue("bindingPaths", out var bindingPathsValue) || bindingPathsValue == null)
        {
            return Array.Empty<string>();
        }

        return bindingPathsValue switch
        {
            string[] paths => paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray(),
            IEnumerable<string> paths => paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray(),
            IEnumerable<object?> rawPaths => rawPaths
                .Select(path => path?.ToString())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToArray(),
            _ => Array.Empty<string>()
        };
    }

    private sealed record BindingPathMatch(
        string? BindingPath,
        string Confidence,
        string MatchStrategy,
        int MatchRank);

    private enum PathMatchKind
    {
        None,
        Exact,
        TerminalSegment
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
