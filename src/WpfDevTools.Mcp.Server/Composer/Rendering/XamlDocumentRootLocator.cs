namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal static class XamlDocumentRootLocator
{
    internal static int FindStart(string xaml)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        var index = 0;
        while (index < xaml.Length)
        {
            var start = xaml.IndexOf('<', index);
            if (start < 0 || start + 1 >= xaml.Length)
            {
                return -1;
            }

            if (xaml.AsSpan(start).StartsWith("<!--", StringComparison.Ordinal))
            {
                index = FindTerminator(xaml, start + 4, "-->");
                if (index < 0)
                {
                    return -1;
                }

                continue;
            }

            if (xaml.AsSpan(start).StartsWith("<?", StringComparison.Ordinal))
            {
                index = FindTerminator(xaml, start + 2, "?>");
                if (index < 0)
                {
                    return -1;
                }

                continue;
            }

            if (xaml.AsSpan(start).StartsWith("<!", StringComparison.Ordinal))
            {
                index = FindMarkupDeclarationEnd(xaml, start + 2);
                if (index < 0)
                {
                    return -1;
                }

                continue;
            }

            if (xaml[start + 1] == '/')
            {
                index = start + 2;
                continue;
            }

            return start;
        }

        return -1;
    }

    private static int FindTerminator(string xaml, int start, string terminator)
    {
        var end = xaml.IndexOf(terminator, start, StringComparison.Ordinal);
        return end < 0 ? -1 : end + terminator.Length;
    }

    private static int FindMarkupDeclarationEnd(string xaml, int start)
    {
        var bracketDepth = 0;
        var quote = '\0';
        for (var index = start; index < xaml.Length; index++)
        {
            if (quote != '\0')
            {
                if (xaml[index] == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (xaml.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = FindTerminator(xaml, index + 4, "-->");
                if (commentEnd < 0)
                {
                    return -1;
                }

                index = commentEnd - 1;
                continue;
            }

            switch (xaml[index])
            {
                case '\'':
                case '"':
                    quote = xaml[index];
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case '>' when bracketDepth == 0:
                    return index + 1;
            }
        }

        return -1;
    }
}
