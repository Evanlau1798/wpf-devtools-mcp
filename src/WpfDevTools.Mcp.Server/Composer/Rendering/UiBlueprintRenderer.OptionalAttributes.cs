using System.Text;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static readonly Regex MarkupSegmentPattern = new(
        """<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<(?!!|/|\?)(?:[^"'<>]|"[^"]*"|'[^']*')+>""",
        RegexOptions.CultureInvariant);
    internal static string OmitUnsetPropertyAttributes(
        string template,
        UiBlueprintNode node,
        UiBlockDefinition block)
        => MarkupSegmentPattern.Replace(template, segment =>
        {
            if (segment.Value.StartsWith("<!", StringComparison.Ordinal))
            {
                return segment.Value;
            }

            return OmitUnsetAttributesFromStartTag(segment.Value, node, block);
        });

    private static string OmitUnsetAttributesFromStartTag(
        string tag,
        UiBlueprintNode node,
        UiBlockDefinition block)
    {
        var removals = new List<(int Start, int Length)>();
        var index = 1;
        while (index < tag.Length && IsAttributeNameCharacter(tag[index]))
        {
            index++;
        }

        while (index < tag.Length)
        {
            var attributeStart = index;
            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
            {
                index++;
            }

            if (index >= tag.Length || tag[index] is '>' or '/')
            {
                break;
            }

            var nameStart = index;
            while (index < tag.Length && IsAttributeNameCharacter(tag[index]))
            {
                index++;
            }

            if (index == nameStart)
            {
                break;
            }

            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
            {
                index++;
            }

            if (index >= tag.Length || tag[index++] != '=')
            {
                break;
            }

            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
            {
                index++;
            }

            if (index >= tag.Length || tag[index] is not ('\'' or '"'))
            {
                break;
            }

            var quote = tag[index++];
            var valueStart = index;
            index = tag.IndexOf(quote, index);
            if (index < 0)
            {
                break;
            }

            var value = tag[valueStart..index++];
            var token = TokenPattern.Match(value);
            if (token.Success && token.Length == value.Length)
            {
                var propertyName = token.Groups["name"].Value;
                var propertyValue = GetPropertyValue(node, propertyName) ?? GetDefaultPropertyValue(block, propertyName);
                if (block.Properties.ContainsKey(propertyName) && propertyValue is null)
                {
                    removals.Add((attributeStart, index - attributeStart));
                }
            }
        }

        if (removals.Count == 0)
        {
            return tag;
        }

        var result = new StringBuilder(tag);
        foreach (var (start, length) in removals.AsEnumerable().Reverse())
        {
            result.Remove(start, length);
        }

        return result.ToString();
    }

    private static bool IsAttributeNameCharacter(char value)
        => char.IsLetterOrDigit(value) || value is '_' or '.' or ':' or '-';
}
