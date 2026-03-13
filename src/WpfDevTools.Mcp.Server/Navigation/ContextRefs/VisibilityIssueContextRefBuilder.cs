using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.ContextRefs;

internal static class VisibilityIssueContextRefBuilder
{
    public static ToolNavigationReference Create(string elementId, string rootCause) =>
        ToolNavigationReference.Create(
            "visibility-issue",
            ("elementId", elementId),
            ("rootCause", rootCause));
}
