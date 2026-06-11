namespace WpfDevTools.Mcp.Server.Schema;

public sealed record ToolNavigationEnvelope(
    IReadOnlyList<ToolNextStep> Recommended,
    IReadOnlyList<ToolNextStep> Alternatives,
    IReadOnlyList<string> PrefetchTools,
    IReadOnlyList<ToolNavigationReference> ContextRefs)
{
    public static ToolNavigationEnvelope Empty { get; } = new([], [], [], []);

    public static ToolNavigationEnvelope FromRecommended(
        IReadOnlyList<ToolNextStep>? recommended,
        IReadOnlyList<ToolNextStep>? alternatives = null,
        IReadOnlyList<string>? prefetchTools = null,
        IReadOnlyList<ToolNavigationReference>? contextRefs = null) =>
        new(
            recommended ?? [],
            alternatives ?? [],
            prefetchTools ?? [],
            contextRefs ?? []);
}
