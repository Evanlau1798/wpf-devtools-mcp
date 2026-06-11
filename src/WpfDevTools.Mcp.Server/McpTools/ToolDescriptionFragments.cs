namespace WpfDevTools.Mcp.Server.McpTools;

internal static class ToolDescriptionFragments
{
    public const string ConnectPrerequisite =
        "PREREQUISITE: connect() or connect(processId) must have succeeded for the target process.\n\n";

    public const string ContractGuidance =
        "CONTRACT: Canonical payload lives in structuredContent. content[0].text is a compact fallback summary, not the full result. Read wpf://contracts/response for stable fields and MCP envelope semantics.\n\n";
}
