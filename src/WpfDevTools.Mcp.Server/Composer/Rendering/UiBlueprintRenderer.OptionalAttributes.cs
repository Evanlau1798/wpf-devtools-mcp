using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static readonly Regex OptionalAttributePattern = new(
        "\\s+[A-Za-z_][A-Za-z0-9_.:-]*=\\\"\\{\\{\\s*(?<name>[A-Za-z0-9_.-]+)\\s*\\}\\}\\\"",
        RegexOptions.CultureInvariant);

    private static string OmitUnsetPropertyAttributes(
        string template,
        UiBlueprintNode node,
        UiBlockDefinition block)
        => OptionalAttributePattern.Replace(template, match =>
        {
            var propertyName = match.Groups["name"].Value;
            if (!block.Properties.ContainsKey(propertyName))
            {
                return match.Value;
            }

            var value = GetPropertyValue(node, propertyName) ?? GetDefaultPropertyValue(block, propertyName);
            return value is null ? string.Empty : match.Value;
        });
}
