namespace WpfDevTools.Mcp.Server.Schema;

public static class NavigationLoadHint
{
    public static IReadOnlyList<string>? ToolNames(params string?[] toolNames)
    {
        var normalized = toolNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0 ? null : normalized;
    }
}
