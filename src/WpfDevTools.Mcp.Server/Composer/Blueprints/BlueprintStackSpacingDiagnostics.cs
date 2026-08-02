using System.Globalization;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintStackSpacingDiagnostics
{
    private const double LargeSpacingThreshold = 96;
    private const int MaxAdvisories = 32;

    internal static void AddIssues(
        UiBlueprintNode root,
        string rootPath,
        List<BlueprintValidationIssue> warnings)
    {
        var added = 0;
        AddIssues(root, rootPath, warnings, ref added);
    }

    private static void AddIssues(
        UiBlueprintNode node,
        string path,
        List<BlueprintValidationIssue> warnings,
        ref int added)
    {
        if (added >= MaxAdvisories)
        {
            return;
        }

        if (node.Kind == "core.stack"
            && node.Slots.TryGetValue("children", out var stackChildren)
            && stackChildren.Length > 1)
        {
            var horizontal = ReadString(node, "orientation", "Vertical") == "Horizontal";
            for (var index = 1; index < stackChildren.Length && added < MaxAdvisories; index++)
            {
                var child = stackChildren[index];
                if (TryReadLeadingMargin(child, horizontal, out var spacing)
                    && spacing >= LargeSpacingThreshold)
                {
                    var childPath = $"{path}.slots.children[{index}]";
                    warnings.Add(new BlueprintValidationIssue(
                        $"{childPath}.properties.margin",
                        "LargeFixedStackSpacing",
                        $"A {spacing.ToString("0.##", CultureInfo.InvariantCulture)} DIP leading margin may be distributing content inside a {(horizontal ? "horizontal" : "vertical")} Stack.",
                        "Use core.grid with Auto/* rows or columns for distribution, and reserve margin for local spacing.",
                        [],
                        [],
                        "children")
                    {
                        RelatedJsonPaths = [path]
                    });
                    added++;
                }
            }
        }

        foreach (var (slotName, children) in node.Slots)
        {
            for (var index = 0; index < children.Length; index++)
            {
                AddIssues(
                    children[index],
                    $"{AppendJsonPath(path + ".slots", slotName)}[{index}]",
                    warnings,
                    ref added);
            }
        }
    }

    private static bool TryReadLeadingMargin(UiBlueprintNode node, bool horizontal, out double spacing)
    {
        spacing = 0;
        if (!node.Properties.TryGetValue("margin", out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out spacing) && double.IsFinite(spacing);
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var parts = (value.GetString() ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not (1 or 2 or 4)
            || parts.Any(part => !double.TryParse(
                part,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _)))
        {
            return false;
        }

        var values = parts.Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        spacing = horizontal ? values[0] : values.Length switch
        {
            1 => values[0],
            2 => values[1],
            _ => values[1]
        };
        return double.IsFinite(spacing);
    }

    private static string ReadString(UiBlueprintNode node, string propertyName, string defaultValue)
        => node.Properties.TryGetValue(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;

    private static string AppendJsonPath(string path, string propertyName)
        => propertyName.Length > 0
           && (char.IsLetter(propertyName[0]) || propertyName[0] == '_')
           && propertyName.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_')
            ? $"{path}.{propertyName}"
            : $"{path}[{JsonSerializer.Serialize(propertyName)}]";
}
