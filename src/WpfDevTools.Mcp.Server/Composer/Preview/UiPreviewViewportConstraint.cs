using System.Globalization;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class UiPreviewViewportConstraint
{
    internal static string Apply(
        string xaml,
        string rootTag,
        int? width,
        int? height)
    {
        if (width is null && height is null)
        {
            return xaml;
        }

        var rootStart = XamlDocumentRootLocator.FindStart(xaml);
        var rootEnd = rootStart < 0 ? -1 : FindTagEnd(xaml, rootStart + 1);
        if (rootEnd < 0)
        {
            return xaml;
        }

        var openingTag = xaml[rootStart..(rootEnd + 1)];
        if (!openingTag.AsSpan(1).StartsWith(rootTag, StringComparison.Ordinal))
        {
            return xaml;
        }

        openingTag = SetAttribute(openingTag, "Width", width);
        openingTag = SetAttribute(openingTag, "Height", height);
        return string.Concat(xaml.AsSpan(0, rootStart), openingTag, xaml.AsSpan(rootEnd + 1));
    }

    private static string SetAttribute(string openingTag, string name, int? value)
    {
        if (value is null)
        {
            return openingTag;
        }

        var replacement = $" {name}=\"{value.Value.ToString(CultureInfo.InvariantCulture)}\"";
        if (TryFindAttributeRange(openingTag, name, out var start, out var length))
        {
            return openingTag.Remove(start, length).Insert(start, replacement);
        }

        var insertionIndex = openingTag.EndsWith("/>", StringComparison.Ordinal)
            ? openingTag.Length - 2
            : openingTag.Length - 1;
        return openingTag.Insert(insertionIndex, replacement);
    }

    private static bool TryFindAttributeRange(
        string openingTag,
        string expectedName,
        out int start,
        out int length)
    {
        var index = 1;
        while (index < openingTag.Length
               && !char.IsWhiteSpace(openingTag[index])
               && openingTag[index] is not '>' and not '/')
        {
            index++;
        }

        while (index < openingTag.Length)
        {
            var whitespaceStart = index;
            while (index < openingTag.Length && char.IsWhiteSpace(openingTag[index]))
            {
                index++;
            }

            if (index >= openingTag.Length || openingTag[index] is '>' or '/')
            {
                break;
            }

            var nameStart = index;
            while (index < openingTag.Length
                   && !char.IsWhiteSpace(openingTag[index])
                   && openingTag[index] is not '=' and not '>' and not '/')
            {
                index++;
            }

            var attributeName = openingTag[nameStart..index];
            while (index < openingTag.Length && char.IsWhiteSpace(openingTag[index]))
            {
                index++;
            }

            if (index < openingTag.Length && openingTag[index] == '=')
            {
                index++;
                while (index < openingTag.Length && char.IsWhiteSpace(openingTag[index]))
                {
                    index++;
                }

                if (index < openingTag.Length && openingTag[index] is '"' or '\'')
                {
                    var quote = openingTag[index++];
                    while (index < openingTag.Length && openingTag[index] != quote)
                    {
                        index++;
                    }

                    if (index < openingTag.Length)
                    {
                        index++;
                    }
                }
                else
                {
                    while (index < openingTag.Length
                           && !char.IsWhiteSpace(openingTag[index])
                           && openingTag[index] is not '>' and not '/')
                    {
                        index++;
                    }
                }
            }

            if (string.Equals(attributeName, expectedName, StringComparison.Ordinal))
            {
                start = whitespaceStart;
                length = index - whitespaceStart;
                return true;
            }
        }

        start = -1;
        length = 0;
        return false;
    }

    private static int FindTagEnd(string xaml, int start)
    {
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

            if (xaml[index] is '"' or '\'')
            {
                quote = xaml[index];
            }
            else if (xaml[index] == '>')
            {
                return index;
            }
        }

        return -1;
    }
}
