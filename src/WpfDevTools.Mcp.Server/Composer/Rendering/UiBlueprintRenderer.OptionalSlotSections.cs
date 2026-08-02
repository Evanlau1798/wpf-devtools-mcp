using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static readonly Regex OptionalSlotSectionPattern = new(
        @"\{\{\s*\?\s*(?<name>slot\.[A-Za-z0-9_.-]+)\s*\}\}(?<content>.*?)\{\{\s*/\s*\k<name>\s*\}\}",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex OptionalSlotMarkerPattern = new(
        @"\{\{\s*(?<marker>[?/])\s*(?<name>slot\.[A-Za-z0-9_.-]+)\s*\}\}",
        RegexOptions.CultureInvariant);
    private static readonly Regex OptionalSlotMarkerStartPattern = new(
        @"\{\{\s*[?/]\s*slot\.",
        RegexOptions.CultureInvariant);

    private static string ResolveOptionalSlotSections(
        string template,
        UiBlueprintNode node,
        UiBlockDefinition block,
        string path,
        List<BlueprintValidationIssue> errors)
    {
        var markers = OptionalSlotMarkerPattern.Matches(template);
        var openName = string.Empty;
        var malformed = markers.Count != OptionalSlotMarkerStartPattern.Matches(template).Count;
        foreach (Match marker in markers)
        {
            var name = marker.Groups["name"].Value;
            if (marker.Groups["marker"].Value == "?")
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
            errors.Add(Issue(path, "RendererOptionalSectionMalformed", $"Renderer for block '{block.Kind}' contains unmatched or nested optional slot markers.", "Use non-nested pairs such as {{?slot.name}}...{{/slot.name}}."));
            return string.Empty;
        }

        return OptionalSlotSectionPattern.Replace(template, match =>
        {
            var token = match.Groups["name"].Value;
            var slotName = token["slot.".Length..];
            if (!block.Slots.ContainsKey(slotName))
            {
                errors.Add(Issue(path, "RendererTokenMismatch", $"Renderer optional section '{token}' does not match a slot on block '{block.Kind}'.", "Update the renderer template section or add the slot to the block contract."));
                return string.Empty;
            }

            return node.Slots.TryGetValue(slotName, out var children) && children.Length > 0
                ? match.Groups["content"].Value
                : string.Empty;
        });
    }
}
