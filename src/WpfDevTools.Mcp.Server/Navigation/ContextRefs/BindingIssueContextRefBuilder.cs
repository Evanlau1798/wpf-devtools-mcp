using System.Text.Json;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.ContextRefs;

internal static class BindingIssueContextRefBuilder
{
    public static ToolNavigationReference? TryBuild(JsonElement issue, string diagnosis)
    {
        if (!TryGetString(issue, "elementId", out var elementId))
        {
            return null;
        }

        return ToolNavigationReference.Create(
            "binding-issue",
            ("elementId", elementId),
            ("propertyName", TryGetOptionalString(issue, "propertyName")),
            ("bindingPath", TryGetOptionalString(issue, "bindingPath")),
            ("diagnosis", diagnosis));
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            var candidate = property.GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string? TryGetOptionalString(JsonElement element, string propertyName) =>
        TryGetString(element, propertyName, out var value) ? value : null;
}
