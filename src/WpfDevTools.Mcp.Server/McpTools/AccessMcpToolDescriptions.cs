namespace WpfDevTools.Mcp.Server.McpTools;

internal static class AccessMcpToolDescriptions
{
    internal const string GetStatus =
        "Reports session access and exact request inputs; never grants it.\n\nCATEGORY: Process";

    internal const string Request =
        "Requests exact temporary access through server elicitation; Agent text is not authorization.\n\nCATEGORY: Process";
}
