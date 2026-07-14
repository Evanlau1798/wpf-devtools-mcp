using System.Text.RegularExpressions;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal static class RendererIdentityTargetValidator
{
    private static readonly Regex StaticIdentityPattern = new(
        @"(?:^|\s)(?:x:Name|Name|AutomationProperties\.AutomationId)\s*=",
        RegexOptions.CultureInvariant);

    public static bool IsValidPlacement(string template, int tokenIndex, int tokenLength)
    {
        var tagStart = template.LastIndexOf('<', tokenIndex);
        if (tagStart < 0 || template.LastIndexOf('>', tokenIndex) > tagStart)
        {
            return false;
        }

        var tagEnd = FindTagEnd(template, tagStart);
        if (tagEnd < tokenIndex + tokenLength)
        {
            return false;
        }

        var prefix = template[(tagStart + 1)..tokenIndex];
        var first = prefix.AsSpan().TrimStart();
        return !first.IsEmpty
            && first[0] is not '/' and not '!' and not '?'
            && !IsInsideAttributeValue(prefix);
    }

    public static bool HasStaticIdentityConflict(
        string template,
        int tokenIndex,
        int tokenLength)
    {
        var tagStart = template.LastIndexOf('<', tokenIndex);
        var tagEnd = FindTagEnd(template, tagStart);
        var startTag = string.Concat(
            template.AsSpan(tagStart + 1, tokenIndex - tagStart - 1),
            template.AsSpan(tokenIndex + tokenLength, tagEnd - tokenIndex - tokenLength));
        return StaticIdentityPattern.IsMatch(startTag);
    }

    private static int FindTagEnd(string template, int tagStart)
    {
        char quote = '\0';
        for (var index = tagStart + 1; index < template.Length; index++)
        {
            var current = template[index];
            if (current is '\'' or '"')
            {
                quote = quote == '\0' ? current : quote == current ? '\0' : quote;
            }
            else if (current == '>' && quote == '\0')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsInsideAttributeValue(string prefix)
    {
        char quote = '\0';
        foreach (var current in prefix)
        {
            if (current is '\'' or '"')
            {
                quote = quote == '\0' ? current : quote == current ? '\0' : quote;
            }
        }

        return quote != '\0';
    }
}
