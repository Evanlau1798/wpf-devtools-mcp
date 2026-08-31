using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class PreviewVisualLayoutContractParser
{
    internal const int MaximumRegions = 16;

    internal static bool TryParse(
        string? json,
        out PreviewVisualLayoutContract? contract,
        out string? error)
    {
        contract = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        PreviewVisualLayoutContractDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PreviewVisualLayoutContractDocument>(json);
        }
        catch (JsonException exception)
        {
            error = $"visualLayoutContractJson is not valid JSON: {exception.Message}";
            return false;
        }

        if (document is null || HasUnknownFields(document.UnknownFields))
        {
            error = "visualLayoutContractJson must contain only the supported root field 'regions'.";
            return false;
        }

        if (document.Regions is null || document.Regions.Length == 0)
        {
            error = "visualLayoutContractJson.regions must contain at least one region.";
            return false;
        }

        if (document.Regions.Length > MaximumRegions)
        {
            error = $"visualLayoutContractJson.regions supports at most {MaximumRegions} regions.";
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var regions = new List<PreviewVisualLayoutRegion>(document.Regions.Length);
        for (var index = 0; index < document.Regions.Length; index++)
        {
            var source = document.Regions[index];
            var path = $"visualLayoutContractJson.regions[{index}]";
            if (source is null
                || HasUnknownFields(source.UnknownFields)
                || source.Bounds is null
                || HasUnknownFields(source.Bounds.UnknownFields))
            {
                error = $"{path} contains an unsupported field or is missing bounds.";
                return false;
            }

            var elementName = source.ElementName?.Trim();
            if (string.IsNullOrWhiteSpace(elementName) || elementName.Length > 128)
            {
                error = $"{path}.elementName must contain 1 to 128 characters.";
                return false;
            }

            if (!names.Add(elementName))
            {
                error = $"{path}.elementName duplicates '{elementName}'.";
                return false;
            }

            if (!TryValidateBounds(source.Bounds, path, out var boundsError))
            {
                error = boundsError;
                return false;
            }

            var tolerance = source.Tolerance ?? 0.05;
            if (!double.IsFinite(tolerance) || tolerance is < 0 or > 0.25)
            {
                error = $"{path}.tolerance must be between 0 and 0.25.";
                return false;
            }

            if (!TryNormalizeChrome(source.HorizontalScrollbarChrome, path, "horizontalScrollbarChrome", out var horizontal, out error)
                || !TryNormalizeChrome(source.VerticalScrollbarChrome, path, "verticalScrollbarChrome", out var vertical, out error))
            {
                return false;
            }

            regions.Add(new PreviewVisualLayoutRegion(
                elementName,
                new PreviewNormalizedBounds(
                    source.Bounds.X!.Value,
                    source.Bounds.Y!.Value,
                    source.Bounds.Width!.Value,
                    source.Bounds.Height!.Value),
                tolerance,
                horizontal,
                vertical));
        }

        contract = new PreviewVisualLayoutContract(regions);
        return true;
    }

    private static bool TryValidateBounds(
        PreviewNormalizedBoundsDocument bounds,
        string path,
        out string? error)
    {
        error = null;
        if (bounds.X is not { } x
            || bounds.Y is not { } y
            || bounds.Width is not { } width
            || bounds.Height is not { } height)
        {
            error = $"{path}.bounds must explicitly provide x, y, width, and height.";
            return false;
        }

        var values = new[] { x, y, width, height };
        if (values.Any(value => !double.IsFinite(value))
            || x < 0 || y < 0
            || width <= 0 || height <= 0
            || x + width > 1
            || y + height > 1)
        {
            error = $"{path}.bounds must stay inside the normalized viewport with positive width and height.";
            return false;
        }

        return true;
    }

    private static bool TryNormalizeChrome(
        string? value,
        string path,
        string propertyName,
        out string normalized,
        out string? error)
    {
        normalized = "any";
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        normalized = value.Trim().ToLowerInvariant();
        if (normalized is "any" or "hidden" or "visible")
        {
            return true;
        }

        error = $"{path}.{propertyName} must be one of: any, hidden, visible.";
        return false;
    }

    private static bool HasUnknownFields(Dictionary<string, JsonElement>? fields)
        => fields is { Count: > 0 };
}

internal static class PreviewVisualLayoutContractAnalyzer
{
    internal static PreviewVisualLayoutContractSummary Analyze(
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics,
        PreviewVisualLayoutContract? contract,
        PreviewLayoutRiskSummary? layoutRiskSummary = null)
    {
        if (contract is null)
        {
            return PreviewVisualLayoutContractSummary.NotProvided;
        }

        var root = diagnostics.FirstOrDefault(item => item.Success && item.Tool == "get_layout_info")?.Payload;
        if (!TryReadLayout(root, requirePosition: false, out var rootLayout)
            || rootLayout.Width <= 0
            || rootLayout.Height <= 0)
        {
            return Unresolved(contract, "Preview root layout was unavailable.");
        }

        var snapshots = diagnostics
            .Where(item => item.Success && item.Tool == "get_element_snapshot")
            .Select(item => item.Payload)
            .Where(payload => TryGetString(payload, "elementName", out _))
            .GroupBy(payload => payload.GetProperty("elementName").GetString()!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var visibility = PreviewVisualLayoutVisibility.Read(layoutRiskSummary);
        var results = contract.Regions.Select(region => AnalyzeRegion(region, rootLayout, snapshots, visibility)).ToArray();
        var mismatchCount = results.Count(item => item.Status == "mismatch");
        var unresolvedCount = results.Count(item => item.Status == "unresolved");
        return new PreviewVisualLayoutContractSummary(
            Provided: true,
            Passed: mismatchCount == 0 && unresolvedCount == 0,
            RegionCount: results.Length,
            EvaluatedRegionCount: results.Count(item => item.ActualBounds is not null),
            MismatchCount: mismatchCount,
            UnresolvedCount: unresolvedCount,
            Regions: results,
            Guidance: mismatchCount == 0 && unresolvedCount == 0
                ? "All declared visual regions match the preview contract, using effective visible bounds when clipping diagnostics are available. Recheck the applied final app."
                : "Repair mismatched or unresolved regions before visual approval; this contract is pack-neutral geometry evidence.");
    }

    private static PreviewVisualLayoutRegionResult AnalyzeRegion(
        PreviewVisualLayoutRegion region,
        LayoutReading root,
        IReadOnlyDictionary<string, JsonElement> snapshots,
        IReadOnlyDictionary<string, PreviewVisualLayoutVisibilityReading> visibility)
    {
        if (!snapshots.TryGetValue(region.ElementName, out var snapshot))
        {
            return Result(
                region,
                "unresolved",
                null,
                null,
                [],
                "Exact runtime element was not resolved.");
        }

        if (!snapshot.TryGetProperty("layout", out var layoutElement)
            || !TryReadLayout(layoutElement, requirePosition: true, out var layout))
        {
            return Result(
                region,
                "unresolved",
                null,
                null,
                [],
                "Runtime layout size or position was unavailable.");
        }

        var actualX = (layout.X - root.X) / root.Width;
        var actualY = (layout.Y - root.Y) / root.Height;
        var actualWidth = layout.Width / root.Width;
        var actualHeight = layout.Height / root.Height;
        var actual = new PreviewNormalizedBounds(
            Round(actualX),
            Round(actualY),
            Round(actualWidth),
            Round(actualHeight));
        PreviewNormalizedBounds? actualVisible = null;
        double? visibleRatio = null;
        if (visibility.TryGetValue(region.ElementName, out var reading))
        {
            actualVisible = PreviewVisualLayoutVisibility.Apply(
                actualX, actualY, layout.Width, layout.Height, root.Width, root.Height, reading);
            visibleRatio = reading.VisibleRatio;
        }

        var comparison = actualVisible ?? actual;
        var deltas = new[]
        {
            Math.Abs(comparison.X - region.Bounds.X),
            Math.Abs(comparison.Y - region.Bounds.Y),
            Math.Abs(comparison.Width - region.Bounds.Width),
            Math.Abs(comparison.Height - region.Bounds.Height)
        };
        var tolerances = new[]
        {
            region.Tolerance,
            region.Tolerance,
            SizeTolerance(region.Bounds.Width, region.Tolerance, root.Width),
            SizeTolerance(region.Bounds.Height, region.Tolerance, root.Height)
        };
        var geometryMismatches = new[] { "x", "y", "width", "height" }
            .Select((name, index) => (Name: name, Delta: deltas[index], Tolerance: tolerances[index]))
            .Where(item => item.Delta > item.Tolerance)
            .Select(item => $"{item.Name} delta {Round(item.Delta)} exceeded tolerance {Round(item.Tolerance)}.")
            .ToArray();
        var maximumDelta = deltas.Max();
        var scrollbarMismatches = ReadScrollbarMismatches(snapshot, region);
        var status = geometryMismatches.Length == 0 && scrollbarMismatches.Count == 0
            ? "matched"
            : "mismatch";
        return Result(
            region,
            status,
            actual,
            Round(maximumDelta),
            scrollbarMismatches,
            reason: geometryMismatches.Length == 0 ? null : string.Join(" ", geometryMismatches),
            actualVisibleBounds: actualVisible,
            visibleRatio: visibleRatio);
    }

    private static double SizeTolerance(
        double expectedSize,
        double absoluteTolerance,
        double rootSize)
        => Math.Min(absoluteTolerance, Math.Max(expectedSize * 0.25, 2 / rootSize));

    private static IReadOnlyList<string> ReadScrollbarMismatches(
        JsonElement snapshot,
        PreviewVisualLayoutRegion region)
    {
        var mismatches = new List<string>();
        CheckChrome(snapshot, "ComputedHorizontalScrollBarVisibility", "horizontal", region.HorizontalScrollbarChrome, mismatches);
        CheckChrome(snapshot, "ComputedVerticalScrollBarVisibility", "vertical", region.VerticalScrollbarChrome, mismatches);
        return mismatches;
    }

    private static void CheckChrome(
        JsonElement snapshot,
        string propertyName,
        string direction,
        string expected,
        ICollection<string> mismatches)
    {
        if (expected == "any")
        {
            return;
        }

        var actual = ReadCurrentPropertyValue(snapshot, propertyName);
        var matched = expected == "visible"
            ? string.Equals(actual, "Visible", StringComparison.OrdinalIgnoreCase)
            : string.Equals(actual, "Hidden", StringComparison.OrdinalIgnoreCase)
              || string.Equals(actual, "Collapsed", StringComparison.OrdinalIgnoreCase);
        if (!matched)
        {
            mismatches.Add($"Expected {direction} scrollbar chrome '{expected}', actual '{actual ?? "unavailable"}'.");
        }
    }

    private static string? ReadCurrentPropertyValue(JsonElement snapshot, string propertyName)
    {
        if (!snapshot.TryGetProperty("properties", out var properties)
            || !properties.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : TryGetString(property, "currentValue", out var value) ? value : null;
    }

    private static PreviewVisualLayoutContractSummary Unresolved(
        PreviewVisualLayoutContract contract,
        string message)
    {
        var regions = contract.Regions
            .Select(region => Result(region, "unresolved", null, null, [], message))
            .ToArray();
        return new(true, false, regions.Length, 0, 0, regions.Length, regions, message);
    }

    private static PreviewVisualLayoutRegionResult Result(
        PreviewVisualLayoutRegion region,
        string status,
        PreviewNormalizedBounds? actualBounds,
        double? maximumDelta,
        IReadOnlyList<string> scrollbarMismatches,
        string? reason = null,
        PreviewNormalizedBounds? actualVisibleBounds = null,
        double? visibleRatio = null)
        => new(
            region.ElementName,
            status,
            region.Bounds,
            actualBounds,
            region.Tolerance,
            maximumDelta,
            region.HorizontalScrollbarChrome,
            region.VerticalScrollbarChrome,
            reason,
            scrollbarMismatches,
            actualVisibleBounds,
            visibleRatio);

    private static bool TryReadLayout(JsonElement? payload, bool requirePosition, out LayoutReading layout)
    {
        layout = default;
        if (payload is not { ValueKind: JsonValueKind.Object } value
            || !TryGetDouble(value, "actualWidth", out var width)
            || !TryGetDouble(value, "actualHeight", out var height))
        {
            return false;
        }

        var x = 0d;
        var y = 0d;
        if (value.TryGetProperty("positionInWindow", out var position)
            && TryGetDouble(position, "x", out var positionX)
            && TryGetDouble(position, "y", out var positionY))
        {
            x = positionX;
            y = positionY;
        }
        else if (requirePosition)
        {
            return false;
        }

        layout = new LayoutReading(x, y, width, height);
        return true;
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetDouble(out value);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private readonly record struct LayoutReading(double X, double Y, double Width, double Height);
}
