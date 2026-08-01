using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintScrollViewportDiagnostics
{
    private const int MaxAdvisories = 32;
    private const string AdvisoryCode = "UnboundedScrollViewport";

    internal static void AddIssues(
        UiBlueprintNode root,
        string rootPath,
        List<BlueprintValidationIssue> warnings)
    {
        var added = 0;
        AddIssues(root, rootPath, null, null, warnings, ref added);
    }

    private static void AddIssues(
        UiBlueprintNode node,
        string path,
        string? unboundedWidthSource,
        string? unboundedHeightSource,
        List<BlueprintValidationIssue> warnings,
        ref int added)
    {
        if (added >= MaxAdvisories)
        {
            return;
        }

        if (node.Kind is not ("core.stack" or "core.border" or "core.scrollViewer"))
        {
            unboundedWidthSource = null;
            unboundedHeightSource = null;
        }

        if (node.Kind == "core.stack")
        {
            if (ReadString(node, "orientation", "Vertical") == "Horizontal")
            {
                unboundedWidthSource = path;
            }
            else
            {
                unboundedHeightSource = path;
            }
        }

        if (node.Kind == "core.scrollViewer")
        {
            var horizontal = unboundedWidthSource is not null
                && ReadString(node, "horizontalScrollBarVisibility", "Auto") != "Disabled";
            var vertical = unboundedHeightSource is not null
                && ReadString(node, "verticalScrollBarVisibility", "Auto") != "Disabled";
            if (horizontal || vertical)
            {
                var axis = horizontal && vertical ? "horizontal and vertical" : horizontal ? "horizontal" : "vertical";
                warnings.Add(new BlueprintValidationIssue(
                    path,
                    AdvisoryCode,
                    $"The Scroll Viewer is measured without a finite {axis} viewport by an ancestor Stack.",
                    "Place it in a parent that receives a finite size, commonly a fixed or star-sized Grid row or column, then verify that extent exceeds viewport and the corresponding offset changes.",
                    [],
                    [],
                    null)
                {
                    RelatedJsonPaths = new[] { unboundedWidthSource, unboundedHeightSource }
                        .OfType<string>()
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                });
                added++;
            }
        }

        foreach (var (slotName, children) in node.Slots)
        {
            var slotPath = AppendJsonPath(path + ".slots", slotName);
            for (var index = 0; index < children.Length; index++)
            {
                AddIssues(
                    children[index],
                    $"{slotPath}[{index}]",
                    unboundedWidthSource,
                    unboundedHeightSource,
                    warnings,
                    ref added);
            }
        }
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
