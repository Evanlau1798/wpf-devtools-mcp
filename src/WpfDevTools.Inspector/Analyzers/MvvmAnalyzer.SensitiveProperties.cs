namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class MvvmAnalyzer
{
    private static readonly HashSet<string> SensitivePropertyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth",
        "cookie",
        "credential",
        "credentials",
        "key",
        "password",
        "pwd",
        "secret",
        "session",
        "token"
    };

    private static readonly HashSet<string> SensitiveCompactPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "apikey",
        "connectionstring",
        "privatekey",
        "secretkey",
        "sessionkey",
        "signingkey"
    };

    private static bool IsSensitivePropertyName(string propertyName)
    {
        var tokens = SplitPropertyNameTokens(propertyName).ToArray();
        if (tokens.Any(token => SensitivePropertyTokens.Contains(token)))
        {
            return true;
        }

        var compactName = string.Concat(tokens);
        return SensitiveCompactPropertyNames.Contains(compactName);
    }

    private static IEnumerable<string> SplitPropertyNameTokens(string propertyName)
    {
        var tokenStart = -1;
        for (var index = 0; index < propertyName.Length; index++)
        {
            var current = propertyName[index];
            if (!char.IsLetterOrDigit(current))
            {
                if (tokenStart >= 0)
                {
                    yield return propertyName.Substring(tokenStart, index - tokenStart);
                    tokenStart = -1;
                }

                continue;
            }

            if (tokenStart < 0)
            {
                tokenStart = index;
                continue;
            }

            var previous = propertyName[index - 1];
            if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
            {
                yield return propertyName.Substring(tokenStart, index - tokenStart);
                tokenStart = index;
            }
        }

        if (tokenStart >= 0)
        {
            yield return propertyName.Substring(tokenStart);
        }
    }
}
