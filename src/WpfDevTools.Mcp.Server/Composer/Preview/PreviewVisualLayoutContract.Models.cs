using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed class PreviewVisualLayoutContractDocument
{
    [JsonPropertyName("regions")]
    public PreviewVisualLayoutRegionDocument[]? Regions { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}

internal sealed class PreviewVisualLayoutRegionDocument
{
    [JsonPropertyName("elementName")]
    public string? ElementName { get; set; }

    [JsonPropertyName("bounds")]
    public PreviewNormalizedBoundsDocument? Bounds { get; set; }

    [JsonPropertyName("tolerance")]
    public double? Tolerance { get; set; }

    [JsonPropertyName("horizontalScrollbarChrome")]
    public string? HorizontalScrollbarChrome { get; set; }

    [JsonPropertyName("verticalScrollbarChrome")]
    public string? VerticalScrollbarChrome { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}

internal sealed class PreviewNormalizedBoundsDocument
{
    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("width")]
    public double? Width { get; set; }

    [JsonPropertyName("height")]
    public double? Height { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}

internal sealed record PreviewVisualLayoutContract(IReadOnlyList<PreviewVisualLayoutRegion> Regions);

internal sealed record PreviewVisualLayoutRegion(
    string ElementName,
    PreviewNormalizedBounds Bounds,
    double Tolerance,
    string HorizontalScrollbarChrome,
    string VerticalScrollbarChrome);

internal sealed record PreviewNormalizedBounds(double X, double Y, double Width, double Height);

internal sealed record PreviewVisualLayoutContractSummary(
    bool Provided,
    bool? Passed,
    int RegionCount,
    int EvaluatedRegionCount,
    int MismatchCount,
    int UnresolvedCount,
    IReadOnlyList<PreviewVisualLayoutRegionResult> Regions,
    string Guidance)
{
    internal static PreviewVisualLayoutContractSummary NotProvided { get; }
        = new(false, null, 0, 0, 0, 0, [], "No visual layout contract was provided.");
}

internal sealed record PreviewVisualLayoutRegionResult(
    string ElementName,
    string Status,
    PreviewNormalizedBounds ExpectedBounds,
    PreviewNormalizedBounds? ActualBounds,
    double Tolerance,
    double? MaximumBoundsDelta,
    string ExpectedHorizontalScrollbarChrome,
    string ExpectedVerticalScrollbarChrome,
    string? Reason,
    IReadOnlyList<string> ScrollbarMismatches,
    PreviewNormalizedBounds? ActualVisibleBounds,
    double? VisibleRatio);
