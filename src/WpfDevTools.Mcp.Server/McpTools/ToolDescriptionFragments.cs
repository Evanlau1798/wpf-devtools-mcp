namespace WpfDevTools.Mcp.Server.McpTools;

internal static class ToolDescriptionFragments
{
    public const string ConnectPrerequisite =
        "PREREQUISITE: connect() selected target.\n\n";

    public const string ContractGuidance =
        "CONTRACT: Use structuredContent; content[0].text is only a compact fallback. Fields/envelope: wpf://contracts/response.\n\n";

    public const string DetailMode =
        "DETAIL: detail=minimal|compact (default)|verbose; standard is an alias. Verbose adds requested/effective input and observedEffect; semantic fallback indicators remain.\n\n";
}
