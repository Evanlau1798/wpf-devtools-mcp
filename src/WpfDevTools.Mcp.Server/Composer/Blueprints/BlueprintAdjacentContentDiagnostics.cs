using System.Globalization;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintAdjacentContentDiagnostics
{
    private const int MaxAdvisories = 32;
    private const string AdvisoryCode = "AdjacentContentWithoutSeparation";

    internal static void AddIssues(
        UiBlueprintNode root,
        string rootPath,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        List<BlueprintValidationIssue> warnings)
    {
        var added = 0;
        AddIssues(root, rootPath, blocks, warnings, ref added);
    }

    private static void AddIssues(
        UiBlueprintNode node,
        string path,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        List<BlueprintValidationIssue> warnings,
        ref int added)
    {
        blocks.TryGetValue(node.Kind, out var block);
        foreach (var (slotName, children) in node.Slots)
        {
            var slotPath = AppendJsonPath(path + ".slots", slotName);
            if (added < MaxAdvisories
                && block is not null
                && block.Slots.TryGetValue(slotName, out var slot)
                && slot.AdjacencyAdvisory is { } advisory
                && MatchesCondition(node, block, advisory))
            {
                AddSlotIssues(
                    node,
                    path,
                    children,
                    slotName,
                    slotPath,
                    block,
                    advisory,
                    blocks,
                    warnings,
                    ref added);
            }

            for (var index = 0; index < children.Length; index++)
            {
                AddIssues(children[index], $"{slotPath}[{index}]", blocks, warnings, ref added);
            }
        }
    }

    private static void AddSlotIssues(
        UiBlueprintNode parent,
        string parentPath,
        IReadOnlyList<UiBlueprintNode> children,
        string slotName,
        string slotPath,
        UiBlockDefinition parentBlock,
        UiSlotAdjacencyAdvisory advisory,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        List<BlueprintValidationIssue> warnings,
        ref int added)
    {
        if (!TryReadHorizontalEdges(
                parent,
                parentBlock,
                advisory.ItemSpacingProperty,
                out var itemLeft,
                out var itemRight))
        {
            return;
        }

        for (var index = 1; index < children.Count && added < MaxAdvisories; index++)
        {
            var previous = children[index - 1];
            var current = children[index];
            if (!HasRole(previous, advisory.ChildRole, blocks, out var previousBlock)
                || !HasRole(current, advisory.ChildRole, blocks, out var currentBlock)
                || !TryReadHorizontalEdges(
                    previous,
                    previousBlock,
                    advisory.ChildMarginProperty,
                    out _,
                    out var previousRight)
                || !TryReadHorizontalEdges(
                    current,
                    currentBlock,
                    advisory.ChildMarginProperty,
                    out var currentLeft,
                    out _)
                || itemLeft + itemRight + previousRight + currentLeft > 0)
            {
                continue;
            }

            var previousPath = $"{slotPath}[{index - 1}]";
            var currentPath = $"{slotPath}[{index}]";
            warnings.Add(new BlueprintValidationIssue(
                currentPath,
                AdvisoryCode,
                advisory.Message,
                advisory.RepairSuggestion,
                [],
                [],
                slotName)
            {
                RelatedJsonPaths = BuildRelatedPaths(
                    parentPath,
                    previousPath,
                    currentPath,
                    advisory,
                    previousBlock,
                    currentBlock)
            });
            added++;
        }
    }

    private static bool MatchesCondition(
        UiBlueprintNode node,
        UiBlockDefinition block,
        UiSlotAdjacencyAdvisory advisory)
        => TryResolveProperty(node, block, advisory.WhenProperty, out var value)
           && value.ValueKind == JsonValueKind.String
           && advisory.WhenValues.Contains(value.GetString() ?? string.Empty, StringComparer.Ordinal);

    private static bool HasRole(
        UiBlueprintNode node,
        string role,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        out UiBlockDefinition block)
    {
        if (blocks.TryGetValue(node.Kind, out block!)
            && block.AuthoringRoles.Contains(role, StringComparer.Ordinal))
        {
            return true;
        }

        block = new UiBlockDefinition();
        return false;
    }

    private static bool TryReadHorizontalEdges(
        UiBlueprintNode node,
        UiBlockDefinition block,
        string propertyName,
        out double left,
        out double right)
    {
        left = 0;
        right = 0;
        if (string.IsNullOrWhiteSpace(propertyName)
            || !block.Properties.TryGetValue(propertyName, out var property))
        {
            return true;
        }

        if (!string.Equals(property.Format, "thickness", StringComparison.Ordinal))
        {
            return false;
        }
        if (!TryResolveProperty(node, block, propertyName, out var value))
        {
            return true;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var parts = value.GetString()!.Split(',', StringSplitOptions.TrimEntries);
        if (!parts.All(part => double.TryParse(
                part,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _)))
        {
            return false;
        }

        var values = parts.Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        (left, right) = values.Length switch
        {
            1 => (values[0], values[0]),
            2 => (values[0], values[0]),
            4 => (values[0], values[2]),
            _ => (0, 0)
        };
        return values.Length is 1 or 2 or 4;
    }

    private static bool TryResolveProperty(
        UiBlueprintNode node,
        UiBlockDefinition block,
        string propertyName,
        out JsonElement value)
    {
        if (node.Properties.TryGetValue(propertyName, out value))
        {
            return true;
        }

        if (block.Properties.TryGetValue(propertyName, out var property)
            && property.Default is JsonElement defaultValue)
        {
            value = defaultValue;
            return true;
        }

        value = default;
        return false;
    }

    private static string[] BuildRelatedPaths(
        string parentPath,
        string previousPath,
        string currentPath,
        UiSlotAdjacencyAdvisory advisory,
        UiBlockDefinition previousBlock,
        UiBlockDefinition currentBlock)
    {
        var paths = new List<string> { previousPath };
        if (!string.IsNullOrWhiteSpace(advisory.ItemSpacingProperty))
        {
            paths.Add(AppendJsonPath(parentPath + ".properties", advisory.ItemSpacingProperty));
        }
        if (HasThicknessProperty(previousBlock, advisory.ChildMarginProperty))
        {
            paths.Add(AppendJsonPath(previousPath + ".properties", advisory.ChildMarginProperty));
        }
        if (HasThicknessProperty(currentBlock, advisory.ChildMarginProperty))
        {
            paths.Add(AppendJsonPath(currentPath + ".properties", advisory.ChildMarginProperty));
        }

        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool HasThicknessProperty(UiBlockDefinition block, string propertyName)
        => block.Properties.TryGetValue(propertyName, out var property)
           && string.Equals(property.Format, "thickness", StringComparison.Ordinal);

    private static string AppendJsonPath(string path, string propertyName)
        => IsSimpleJsonPathName(propertyName)
            ? $"{path}.{propertyName}"
            : $"{path}[{JsonSerializer.Serialize(propertyName)}]";

    private static bool IsSimpleJsonPathName(string value)
        => value.Length > 0
           && (char.IsLetter(value[0]) || value[0] == '_')
           && value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
}
