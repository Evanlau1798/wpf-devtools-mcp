using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static readonly Regex MarkupSegmentPattern = new(
        """<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<(?!!|/|\?)(?:[^"'<>]|"[^"]*"|'[^']*')+>""",
        RegexOptions.CultureInvariant);
    private static readonly Regex OptionalAttributePattern = new(
        """\s+[A-Za-z_][A-Za-z0-9_.:-]*\s*=\s*(?<quote>["'])\{\{\s*(?<name>[A-Za-z0-9_.-]+)\s*\}\}\k<quote>""",
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

            return OptionalAttributePattern.Replace(segment.Value, attribute =>
            {
                var propertyName = attribute.Groups["name"].Value;
                if (!block.Properties.ContainsKey(propertyName))
                {
                    return attribute.Value;
                }

                var value = GetPropertyValue(node, propertyName) ?? GetDefaultPropertyValue(block, propertyName);
                return value is null ? string.Empty : attribute.Value;
            });
        });
}
