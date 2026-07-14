using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class PreviewLayoutRiskAnalyzer
{
    private const int MaxWarnings = 32;

    internal static PreviewLayoutRiskSummary Analyze(
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics,
        IReadOnlyList<RenderElementCorrelation> correlations)
    {
        var namesByElementId = BuildElementNameMap(diagnostics);
        var correlationsByName = correlations
            .GroupBy(item => item.ElementName, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var clipped = ReadClippingResults(diagnostics)
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
            warnings);
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
        return new PreviewLayoutWarning(
            "RuntimeClippingDetected",
            correlation.JsonPath,
            correlation.BlockKind,
            elementName,
            elementId,
            ReadString(result, "clippingSource") ?? "unknown",
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
