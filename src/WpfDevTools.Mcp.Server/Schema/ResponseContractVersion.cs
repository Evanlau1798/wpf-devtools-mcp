namespace WpfDevTools.Mcp.Server.Schema;

public static class ResponseContractVersion
{
    public const string Current = "2026-03-13-ai-friendly-v2";

    public static IReadOnlyList<string> DeprecatedAliases { get; } =
    [
        "currentValue -> effectiveValue",
        "typeName -> viewModelType",
        "avgRenderTime -> averageFrameTime",
        "count -> totalCount",
        "renderTimeMs -> renderTime"
    ];
}
