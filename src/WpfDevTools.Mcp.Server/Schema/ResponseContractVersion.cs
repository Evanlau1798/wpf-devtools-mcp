namespace WpfDevTools.Mcp.Server.Schema;

public static class ResponseContractVersion
{
    public const string Current = "2026-03-18-ai-friendly-v7";

    public static IReadOnlyList<string> DeprecatedAliases { get; } =
    [
        "currentValue -> effectiveValue",
        "typeName -> viewModelType",
        "avgRenderTime -> averageFrameTime",
        "count -> totalCount",
        "renderTimeMs -> renderTime"
    ];
}
