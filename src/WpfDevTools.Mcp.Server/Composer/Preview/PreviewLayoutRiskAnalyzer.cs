using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class PreviewLayoutRiskAnalyzer
{
    private const int MaxWarnings = 32;
    private const int MaxCoverageDetails = 32;

    internal static PreviewLayoutRiskSummary Analyze(
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics,
        IReadOnlyList<RenderElementCorrelation> correlations,
        int correlationLookupLimit = UiBlueprintPreviewDiagnosticsBridge.ExistingNameLookupLimit)
    {
        var correlatedElementNames = correlations
            .Select(item => item.ElementName)
            .ToHashSet(StringComparer.Ordinal);
        var ambiguousCorrelationNames = correlations
            .DistinctBy(item => (item.JsonPath, item.BlockKind, item.ElementName))
            .GroupBy(item => item.ElementName, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var correlatedTargetCount = correlatedElementNames.Count;
        var exactNameLookupTruncated = correlations
            .Select(item => item.ElementName)
            .Where(name => !name.StartsWith("WpfDevToolsBp_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Skip(correlationLookupLimit)
            .Any();
        var lookupPlan = UiBlueprintPreviewDiagnosticsBridge.BuildCorrelationLookupPlan(correlations, correlationLookupLimit);
        var searchedElementNames = correlatedElementNames
            .Where(name => lookupPlan.Any(lookup => MatchesLookup(name, lookup)))
            .ToHashSet(StringComparer.Ordinal);
        var runtimeMatches = ReadRuntimeMatches(diagnostics, correlatedElementNames).ToArray();
        var runtimeMatchCounts = runtimeMatches
            .GroupBy(item => item.ElementName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var resolvedTargets = runtimeMatches
            .GroupBy(item => item.ElementName, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(item => !ambiguousCorrelationNames.Contains(item.ElementName))
            .ToArray();
        var resolvedElementIds = resolvedTargets
            .Select(item => item.ElementId)
            .ToHashSet(StringComparer.Ordinal);
        var resolvedCorrelationNames = resolvedTargets
            .Select(item => item.ElementName)
            .ToHashSet(StringComparer.Ordinal);
        var unresolvedCorrelations = correlations
            .Where(item => ambiguousCorrelationNames.Contains(item.ElementName)
                           || !resolvedCorrelationNames.Contains(item.ElementName))
            .DistinctBy(item => (item.JsonPath, item.BlockKind, item.ElementName))
            .ToArray();
        var hasIncompleteSearch = HasIncompleteSearch(diagnostics);
        var reportedUnresolvedCorrelations = unresolvedCorrelations
            .Take(MaxCoverageDetails)
            .Select(item => new PreviewUnresolvedCorrelation(
                item.JsonPath,
                item.BlockKind,
                item.ElementName,
                GetUnresolvedReason(
                    item.ElementName,
                    ambiguousCorrelationNames,
                    searchedElementNames,
                    runtimeMatchCounts,
                    diagnostics)))
            .ToArray();
        var inspectedElementIds = ReadInspectedElementIds(diagnostics);
        var inspectedTargetCount = inspectedElementIds
            .Intersect(resolvedElementIds, StringComparer.Ordinal)
            .Count();
        var correlationsByElementName = correlations
            .ToLookup(item => item.ElementName, StringComparer.Ordinal);
        var uninspectedCorrelations = resolvedTargets
            .DistinctBy(item => (item.ElementId, item.ElementName))
            .Where(item => !ambiguousCorrelationNames.Contains(item.ElementName))
            .Where(item => !inspectedElementIds.Contains(item.ElementId))
            .SelectMany(item => correlationsByElementName[item.ElementName]
                .Select(correlation => new PreviewUninspectedCorrelation(
                    correlation.JsonPath,
                    correlation.BlockKind,
                    item.ElementName,
                    item.ElementId)))
            .ToArray();
        var reportedUninspectedCorrelations = uninspectedCorrelations
            .Take(MaxCoverageDetails)
            .ToArray();
        var namesByElementId = BuildElementNameMap(diagnostics);
        var correlationsByName = correlations
            .GroupBy(item => item.ElementName, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var clipped = ReadClippingResults(diagnostics)
            .Where(item => item.ElementId is not null && resolvedElementIds.Contains(item.ElementId))
            .Where(item => IsClipped(item.Result))
            .ToArray();
        var warnings = clipped
            .Select(item => CreateWarning(
                item.Result,
                item.ElementId,
                namesByElementId,
                correlationsByName))
            .Where(warning => warning is not null)
            .Select(warning => warning!)
            .Take(MaxWarnings)
            .ToArray();

        return new PreviewLayoutRiskSummary(
            clipped.Length,
            warnings.Length,
            clipped.Length > warnings.Length,
            warnings)
        {
            CorrelatedTargetCount = correlatedTargetCount,
            ResolvedTargetCount = resolvedElementIds.Count,
            InspectedTargetCount = inspectedTargetCount,
            InspectionTruncated = exactNameLookupTruncated
                                  || hasIncompleteSearch
                                  || unresolvedCorrelations.Length > 0
                                  || inspectedTargetCount < resolvedElementIds.Count,
            UnresolvedCorrelationCount = unresolvedCorrelations.Length,
            ReportedUnresolvedCorrelationCount = reportedUnresolvedCorrelations.Length,
            UnresolvedCorrelationsTruncated = unresolvedCorrelations.Length > reportedUnresolvedCorrelations.Length,
            UnresolvedCorrelations = reportedUnresolvedCorrelations,
            UninspectedCorrelationCount = uninspectedCorrelations.Length,
            ReportedUninspectedCorrelationCount = reportedUninspectedCorrelations.Length,
            UninspectedCorrelationsTruncated = uninspectedCorrelations.Length > reportedUninspectedCorrelations.Length,
            UninspectedCorrelations = reportedUninspectedCorrelations
        };
    }

    private static IEnumerable<(string ElementId, string ElementName)> ReadRuntimeMatches(
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics,
        IReadOnlySet<string> correlatedElementNames)
        => diagnostics
            .Where(diagnostic => diagnostic.Tool == "find_elements" && diagnostic.Success)
            .SelectMany(diagnostic => ReadArray(diagnostic.Payload, "results"))
            .Where(result => TryReadString(result, "elementId", out _)
                             && TryReadString(result, "elementName", out var elementName)
                             && correlatedElementNames.Contains(elementName))
            .Select(result => (
                ElementId: result.GetProperty("elementId").GetString()!,
                ElementName: result.GetProperty("elementName").GetString()!))
            .Distinct();

    private static bool MatchesLookup(string elementName, PreviewCorrelationLookup lookup)
        => string.Equals(lookup.MatchMode, "exact", StringComparison.Ordinal)
            ? string.Equals(elementName, lookup.Query, StringComparison.Ordinal)
            : elementName.Contains(lookup.Query, StringComparison.Ordinal);

    private static string GetUnresolvedReason(
        string elementName,
        IReadOnlySet<string> ambiguousCorrelationNames,
        IReadOnlySet<string> searchedElementNames,
        IReadOnlyDictionary<string, int> runtimeMatchCounts,
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics)
    {
        if (ambiguousCorrelationNames.Contains(elementName))
        {
            return "ambiguous-authored-name";
        }

        if (!searchedElementNames.Contains(elementName))
        {
            return "lookup-budget";
        }

        if (runtimeMatchCounts.TryGetValue(elementName, out var matchCount) && matchCount > 1)
        {
            return "runtime-match-ambiguous";
        }

        return HasCompletedLookup(elementName, diagnostics)
            ? "runtime-not-found"
            : "search-incomplete";
    }

    private static bool HasCompletedLookup(
        string elementName,
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics)
    {
        var searches = diagnostics.Where(item => item.Tool == "find_elements").ToArray();
        var taggedSearches = searches.Where(item => item.Lookup is not null).ToArray();
        var relevantSearches = taggedSearches.Length == 0
            ? searches
            : taggedSearches.Where(item => MatchesLookup(elementName, item.Lookup!));
        return relevantSearches.Any(item => item.Success && !IsIncompleteSearch(item));
    }

    private static bool IsIncompleteSearch(PreviewRuntimeDiagnostic diagnostic)
        => diagnostic.Payload.ValueKind == JsonValueKind.Object
           && diagnostic.Payload.TryGetProperty("searchComplete", out var searchComplete)
           && searchComplete.ValueKind == JsonValueKind.False;

    private static bool HasIncompleteSearch(IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics)
        => diagnostics.Any(diagnostic =>
            diagnostic.Tool == "find_elements"
            && diagnostic.Success
            && IsIncompleteSearch(diagnostic));

    private static IReadOnlySet<string> ReadInspectedElementIds(
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics)
    {
        var inspected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in diagnostics.Where(item =>
                     item.Tool == "get_clipping_info" && item.Success))
        {
            var results = ReadArray(diagnostic.Payload, "results").ToArray();
            if (results.Length == 0)
            {
                inspected.UnionWith(diagnostic.TargetElementIds);
                continue;
            }

            inspected.UnionWith(results
                .Where(result => result.TryGetProperty("success", out var success)
                                 && success.ValueKind == JsonValueKind.True)
                .Select(result => ReadString(result, "elementId"))
                .Where(elementId => elementId is not null)!);
        }

        return inspected;
    }

    private static Dictionary<string, string> BuildElementNameMap(
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics)
        => diagnostics
            .Where(diagnostic => diagnostic.Tool == "find_elements" && diagnostic.Success)
            .SelectMany(diagnostic => ReadArray(diagnostic.Payload, "results"))
            .Where(result => TryReadString(result, "elementId", out _)
                             && TryReadString(result, "elementName", out _))
            .GroupBy(result => result.GetProperty("elementId").GetString()!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().GetProperty("elementName").GetString()!,
                StringComparer.Ordinal);

    private static IEnumerable<(JsonElement Result, string? ElementId)> ReadClippingResults(
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics.Where(item =>
                     item.Tool == "get_clipping_info" && item.Success))
        {
            var results = ReadArray(diagnostic.Payload, "results").ToArray();
            if (results.Length > 0)
            {
                foreach (var result in results)
                {
                    yield return (result, ReadString(result, "elementId"));
                }

                continue;
            }

            yield return (
                diagnostic.Payload,
                diagnostic.TargetElementIds.Count == 1
                    ? diagnostic.TargetElementIds[0]
                    : null);
        }
    }

    private static PreviewLayoutWarning? CreateWarning(
        JsonElement result,
        string? correlatedElementId,
        IReadOnlyDictionary<string, string> namesByElementId,
        IReadOnlyDictionary<string, RenderElementCorrelation> correlationsByName)
    {
        var elementId = correlatedElementId ?? ReadString(result, "elementId");
        if (string.IsNullOrWhiteSpace(elementId)
            || !namesByElementId.TryGetValue(elementId, out var elementName)
            || !correlationsByName.TryGetValue(elementName, out var correlation))
        {
            return null;
        }

        var overflow = result.TryGetProperty("overflowAmount", out var overflowAmount)
            ? overflowAmount.Clone()
            : JsonSerializer.SerializeToElement(new { left = 0, top = 0, right = 0, bottom = 0 });
        var clippingSource = ReadString(result, "clippingSource") ?? "unknown";
        var visibleContentImpact = ReadString(result, "visibleContentImpact") ?? "not-determined";
        var isStructuralOverflow = clippingSource.EndsWith("layout-clip", StringComparison.Ordinal);
        var requiresVisualConfirmation = string.Equals(
            visibleContentImpact,
            "not-determined",
            StringComparison.Ordinal);
        return new PreviewLayoutWarning(
            isStructuralOverflow ? "RuntimeStructuralOverflowRisk" : "RuntimeClippingDetected",
            correlation.JsonPath,
            correlation.BlockKind,
            elementName,
            elementId,
            clippingSource,
            isStructuralOverflow ? "structural-overflow" : "clipping",
            requiresVisualConfirmation ? "advisory" : "warning",
            visibleContentImpact,
            requiresVisualConfirmation,
            overflow,
            ReadString(result, "suggestedFix"));
    }

    private static bool IsClipped(JsonElement result)
        => result.ValueKind == JsonValueKind.Object
           && result.TryGetProperty("isClipped", out var isClipped)
           && isClipped.ValueKind == JsonValueKind.True;

    private static IEnumerable<JsonElement> ReadArray(JsonElement payload, string propertyName)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty(propertyName, out var array)
           && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
            : [];

    private static string? ReadString(JsonElement element, string propertyName)
        => TryReadString(element, propertyName, out var value) ? value : null;

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }
}
