namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiPackPreviewContractGenerator
{
    private static readonly IReadOnlySet<string> ContentCapableBaseKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "button",
        "contentControl",
        "itemsControl",
        "stackPanel",
        "tabItem",
        "toggleButton",
        "window"
    };
}
