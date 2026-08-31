namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class PreviewVisualLayoutVisibility
{
    internal static PreviewNormalizedBounds Apply(
        double x,
        double y,
        double width,
        double height,
        double rootWidth,
        double rootHeight,
        PreviewVisualLayoutVisibilityReading reading)
    {
        var left = Math.Clamp(reading.Left, 0, width);
        var top = Math.Clamp(reading.Top, 0, height);
        var right = Math.Clamp(reading.Right, 0, width - left);
        var bottom = Math.Clamp(reading.Bottom, 0, height - top);
        return new PreviewNormalizedBounds(
            Round(x + left / rootWidth),
            Round(y + top / rootHeight),
            Round((width - left - right) / rootWidth),
            Round((height - top - bottom) / rootHeight));
    }

    internal static IReadOnlyDictionary<string, PreviewVisualLayoutVisibilityReading> Read(
        PreviewLayoutRiskSummary? summary)
        => (summary?.Warnings ?? [])
            .Where(item => item.RiskClassification == "clipping")
            .Where(item => item.OverflowAmount.ValueKind == System.Text.Json.JsonValueKind.Object)
            .GroupBy(item => item.ElementName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var warning = group.First();
                    return new PreviewVisualLayoutVisibilityReading(
                        ReadDouble(warning, "left"),
                        ReadDouble(warning, "top"),
                        ReadDouble(warning, "right"),
                        ReadDouble(warning, "bottom"),
                        warning.VisibleRatio);
                },
                StringComparer.Ordinal);

    private static double ReadDouble(PreviewLayoutWarning warning, string propertyName)
        => warning.OverflowAmount.TryGetProperty(propertyName, out var property)
           && property.TryGetDouble(out var value)
            ? value
            : 0;

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

internal sealed record PreviewVisualLayoutVisibilityReading(
    double Left,
    double Top,
    double Right,
    double Bottom,
    double? VisibleRatio);
