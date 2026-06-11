using System.Windows;
using System.Windows.Data;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private sealed record AffectedElementMatch(
        object Payload,
        string Confidence,
        string MatchStrategy,
        int MatchRank);

    private sealed record AffectedElementScanResult(
        IReadOnlyList<AffectedElementMatch> Matches,
        IReadOnlyList<object> UnsupportedElements);

    private sealed record AffectedBindingCandidate(
        Dictionary<string, object?> Payload,
        BindingExpressionBase BindingExpression);

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
            var unsupportedElements = new List<object>();
            BindingScanBudget? budget = null;
            if (recursive)
            {
                budget = new BindingScanBudget(
                    DefaultBindingTraversalNodeLimit,
                    DefaultBindingResultLimit,
                    "traversal-node-limit",
                    "result-limit");
                CollectAffectedElementsRecursive(
                    element,
                    propertyName.Trim(),
                    viewModelType,
                    matches,
                    unsupportedElements,
                    budget);
            }
            else
            {
                var scanResult = GetAffectedElementsForSingleElement(
                    element,
                    propertyName.Trim(),
                    viewModelType);
                matches.AddRange(scanResult.Matches);
                unsupportedElements.AddRange(scanResult.UnsupportedElements);
            }

            var summary = SummarizeMatches(matches, unsupportedElements.Count);
            return new
            {
                success = true,
                propertyName = propertyName.Trim(),
                viewModelType,
                confidence = summary.Confidence,
                matchStrategy = summary.MatchStrategy,
                requiresVerification = true,
                affectedCount = matches.Count,
                unsupportedCount = unsupportedElements.Count,
                affectedElements = matches.Select(match => match.Payload).ToList(),
                unsupportedElements,
                truncated = budget?.Truncated ?? false,
                scanBudget = budget?.ToContract(matches.Count + unsupportedElements.Count)
            };
        });
    }

    private void CollectAffectedElementsRecursive(
        DependencyObject element,
        string propertyName,
        string? viewModelType,
        List<AffectedElementMatch> affectedElements,
        List<object> unsupportedElements,
        BindingScanBudget budget)
    {
        var traversal = DependencyObjectTraversal.EnumerateDescendantsAndSelfWithMetadata(
            element,
            maxDepth: 50,
            maxNodes: budget.MaxTraversalNodes);
        foreach (var current in traversal)
        {
            if (!budget.TryTakeTraversalNode())
            {
                break;
            }

            var stopDueToResultLimit = false;
            var scanResult = GetAffectedElementsForSingleElement(current, propertyName, viewModelType);
            foreach (var match in scanResult.Matches)
            {
                if (budget.TryTakeResult())
                {
                    affectedElements.Add(match);
                    if (budget.ResultLimitReached)
                    {
                        stopDueToResultLimit = true;
                        break;
                    }
                }
            }

            if (!stopDueToResultLimit)
            {
                foreach (var unsupported in scanResult.UnsupportedElements)
                {
                    if (budget.TryTakeResult())
                    {
                        unsupportedElements.Add(unsupported);
                        if (budget.ResultLimitReached)
                        {
                            stopDueToResultLimit = true;
                            break;
                        }
                    }
                }
            }

            if (stopDueToResultLimit)
            {
                budget.MarkResultTruncated();
                break;
            }
        }

        if (traversal.Truncated)
        {
            budget.MarkTraversalTruncated();
        }
    }

    private AffectedElementScanResult GetAffectedElementsForSingleElement(
        DependencyObject element,
        string propertyName,
        string? viewModelType)
    {
        var matches = new List<AffectedElementMatch>();
        var unsupportedElements = new List<object>();
        var dataContextType = GetElementDataContextType(element);

        if (!MatchesViewModelType(dataContextType, viewModelType))
        {
            return new AffectedElementScanResult(matches, unsupportedElements);
        }

        foreach (var candidate in GetAffectedElementBindings(element))
        {
            var pathMatch = TryMatchBinding(candidate.Payload, propertyName);
            if (pathMatch == null)
            {
                continue;
            }

            var sourceAnalysis = AnalyzeBindingSource(element, candidate.BindingExpression);
            if (!sourceAnalysis.IsSupported)
            {
                unsupportedElements.Add(BuildUnsupportedElementPayload(
                    element,
                    dataContextType,
                    candidate.Payload,
                    pathMatch,
                    sourceAnalysis));
                continue;
            }

            matches.Add(new AffectedElementMatch(
                Payload: BuildAffectedElementPayload(
                    element,
                    dataContextType,
                    candidate.Payload,
                    pathMatch,
                    sourceAnalysis),
                Confidence: pathMatch.Confidence,
                MatchStrategy: pathMatch.MatchStrategy,
                MatchRank: pathMatch.MatchRank));
        }

        return new AffectedElementScanResult(matches, unsupportedElements);
    }

    private static (string Confidence, string MatchStrategy) SummarizeMatches(
        List<AffectedElementMatch> matches,
        int unsupportedCount)
    {
        if (matches.Count > 0)
        {
            var strongestMatch = matches
                .OrderByDescending(match => match.MatchRank)
                .First();

            return (strongestMatch.Confidence, strongestMatch.MatchStrategy);
        }

        return unsupportedCount > 0
            ? ("low", "source-excluded")
            : ("best-effort", "simple-path-match");
    }

    private static IEnumerable<AffectedBindingCandidate> GetAffectedElementBindings(DependencyObject element)
    {
        var seenProperties = new HashSet<string>();
        var enumerator = element.GetLocalValueEnumerator();
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            var dp = entry.Property;
            if (dp == null || !seenProperties.Add(dp.Name))
            {
                continue;
            }

            var bindingExpression = BindingOperations.GetBindingExpressionBase(element, dp);
            if (bindingExpression == null)
            {
                continue;
            }

            yield return new AffectedBindingCandidate(
                BuildBindingPayload(element, dp, bindingExpression),
                bindingExpression);
        }
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

        var terminalSegment = GetTerminalPathSegment(path!);
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
            .Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanPathSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length == 0 ? null : segments[segments.Length - 1];
    }

    private static string? CleanPathSegment(string segment)
    {
        var bracketIndex = segment.IndexOf('[');
        if (bracketIndex >= 0)
        {
            segment = segment.Substring(0, bracketIndex);
        }

        if (segment.Length > 2 && segment[0] == '(' && segment[segment.Length - 1] == ')')
        {
            segment = segment.Substring(1, segment.Length - 2);
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

    private object BuildAffectedElementPayload(
        DependencyObject element,
        string? dataContextType,
        Dictionary<string, object?> binding,
        BindingPathMatch match,
        BindingSourceAnalysis sourceAnalysis)
    {
        return new
        {
            elementId = _elementFinder.GenerateElementId(element),
            elementType = element.GetType().Name,
            elementName = GetElementName(element),
            dataContextType,
            propertyName = binding.TryGetValue("propertyName", out var targetProperty) ? targetProperty?.ToString() : null,
            bindingPath = match.BindingPath,
            currentValue = binding.TryGetValue("currentValue", out var currentValue) ? currentValue?.ToString() : null,
            status = binding.TryGetValue("status", out var status) ? status?.ToString() : null,
            matchConfidence = match.Confidence,
            sourceClassification = sourceAnalysis.SourceClassification
        };
    }

    private object BuildUnsupportedElementPayload(
        DependencyObject element,
        string? dataContextType,
        Dictionary<string, object?> binding,
        BindingPathMatch match,
        BindingSourceAnalysis sourceAnalysis)
    {
        return new
        {
            elementId = _elementFinder.GenerateElementId(element),
            elementType = element.GetType().Name,
            elementName = GetElementName(element),
            dataContextType,
            propertyName = binding.TryGetValue("propertyName", out var targetProperty) ? targetProperty?.ToString() : null,
            bindingPath = match.BindingPath,
            currentValue = binding.TryGetValue("currentValue", out var currentValue) ? currentValue?.ToString() : null,
            status = binding.TryGetValue("status", out var status) ? status?.ToString() : null,
            matchConfidence = "low",
            sourceClassification = sourceAnalysis.SourceClassification,
            unsupportedReason = sourceAnalysis.UnsupportedReason
        };
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
