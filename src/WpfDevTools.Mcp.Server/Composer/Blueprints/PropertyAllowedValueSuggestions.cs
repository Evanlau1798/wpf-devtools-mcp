namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class PropertyAllowedValueSuggestions
{
    private const int MaxSuggestions = 12;

    public static string[] Select(string attemptedValue, IReadOnlyList<string> allowedValues)
    {
        if (allowedValues.Count <= MaxSuggestions)
        {
            return [.. allowedValues];
        }

        return allowedValues
            .Select(value => new
            {
                Value = value,
                Rank = Rank(value, attemptedValue)
            })
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Value.Length)
            .ThenBy(candidate => candidate.Value, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(candidate => candidate.Value)
            .ToArray();
    }

    private static int Rank(string candidate, string attemptedValue)
        => candidate.StartsWith(attemptedValue, StringComparison.OrdinalIgnoreCase) ? 0
            : candidate.Contains(attemptedValue, StringComparison.OrdinalIgnoreCase) ? 1
            : 2;
}
