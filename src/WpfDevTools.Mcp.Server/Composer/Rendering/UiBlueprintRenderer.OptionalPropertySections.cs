using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static readonly Regex OptionalPropertySectionPattern = new(
        @"\{\{\s*(?<mode>[?^])\s*(?<name>property\.[A-Za-z0-9_.-]+)\s*\}\}(?<content>.*?)\{\{\s*/\s*\k<name>\s*\}\}",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex OptionalPropertyMarkerPattern = new(
        @"\{\{\s*(?<marker>[?^/])\s*(?<name>property\.[A-Za-z0-9_.-]+)\s*\}\}",
        RegexOptions.CultureInvariant);
    private static readonly Regex OptionalPropertyMarkerStartPattern = new(
        @"\{\{\s*[?^/]\s*property\.",
        RegexOptions.CultureInvariant);

    private static string ResolveOptionalPropertySections(
        string template,
        UiBlueprintNode node,
        UiBlockDefinition block,
        string path,
        List<BlueprintValidationIssue> errors)
    {
        var markers = OptionalPropertyMarkerPattern.Matches(template);
        var openName = string.Empty;
        var malformed = markers.Count != OptionalPropertyMarkerStartPattern.Matches(template).Count;
        foreach (Match marker in markers)
        {
            var name = marker.Groups["name"].Value;
            if (marker.Groups["marker"].Value != "/")
            {
                malformed |= openName.Length > 0;
                openName = name;
            }
            else
            {
                malformed |= !string.Equals(openName, name, StringComparison.Ordinal);
                openName = string.Empty;
            }
        }

        if (malformed || openName.Length > 0)
        {
            errors.Add(Issue(path, "RendererOptionalSectionMalformed", $"Renderer for block '{block.Kind}' contains unmatched or nested optional property markers.", "Use non-nested pairs such as {{?property.name}}...{{/property.name}} or {{^property.name}}...{{/property.name}}."));
            return string.Empty;
        }

        return OptionalPropertySectionPattern.Replace(template, match =>
        {
            var token = match.Groups["name"].Value;
            var propertyName = token["property.".Length..];
            if (!block.Properties.ContainsKey(propertyName))
            {
                errors.Add(Issue(path, "RendererTokenMismatch", $"Renderer optional section '{token}' does not match a property on block '{block.Kind}'.", "Update the renderer template section or add the property to the block contract."));
                return string.Empty;
            }

            var value = GetPropertyValue(node, propertyName) ?? GetDefaultPropertyValue(block, propertyName);
            var hasValue = !string.IsNullOrEmpty(value);
            var include = match.Groups["mode"].Value == "?" ? hasValue : !hasValue;
            return include ? match.Groups["content"].Value : string.Empty;
        });
    }
}
