using System.Text.Json.Serialization;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed class BlueprintCompositionMapBuilder(PackRegistry registry)
{
    private const int MaximumReportedTargets = 64;

    public BlueprintCompositionMap Build(string blueprintJson)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
            blueprintJson,
            "<inline-blueprint>",
            UiComposerSchemaVersions.UiBlueprint);
        var declaredPackIds = blueprint.Packs
            .Select(pack => pack.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var blocks = new BlockCatalogService(registry)
            .GetCatalog(new BlockCatalogQuery(PackIds: declaredPackIds))
            .Items
            .GroupBy(item => item.Kind, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var targets = new List<BlueprintCompositionTargetSummary>();
        var totalTargetCount = 0;

        Visit(blueprint.Layout, "$.layout", blocks, targets, ref totalTargetCount);
        return new BlueprintCompositionMap(
            totalTargetCount,
            targets.Count,
            totalTargetCount > targets.Count,
            targets);
    }

    private static void Visit(
        UiBlueprintNode node,
        string jsonPath,
        IReadOnlyDictionary<string, BlockCatalogItem> blocks,
        List<BlueprintCompositionTargetSummary> targets,
        ref int totalTargetCount)
    {
        if (blocks.TryGetValue(node.Kind, out var block))
        {
            foreach (var slot in block.Slots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                totalTargetCount++;
                if (targets.Count < MaximumReportedTargets)
                {
                    targets.Add(CreateTarget(node, jsonPath, slot.Key, slot.Value));
                }
            }
        }

        foreach (var (slotName, children) in node.Slots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var slotPath = BlueprintCompositionTargetPath.AppendProperty(jsonPath + ".slots", slotName);
            for (var index = 0; index < children.Length; index++)
            {
                Visit(children[index], $"{slotPath}[{index}]", blocks, targets, ref totalTargetCount);
            }
        }
    }

    private static BlueprintCompositionTargetSummary CreateTarget(
        UiBlueprintNode node,
        string jsonPath,
        string slotName,
        BlockCatalogSlot slot)
    {
        var currentCount = node.Slots.TryGetValue(slotName, out var children) ? children.Length : 0;
        var targetParent = string.IsNullOrWhiteSpace(node.ElementName) ? jsonPath : "@" + node.ElementName;
        var targetPath = BlueprintCompositionTargetPath.AppendProperty(targetParent + ".slots", slotName);
        return new BlueprintCompositionTargetSummary(
            targetPath,
            jsonPath,
            node.Kind,
            slotName,
            currentCount,
            slot.MinItems,
            slot.MaxItems,
            slot.MaxItems is int maximum ? Math.Max(0, maximum - currentCount) : null);
    }
}

internal sealed record BlueprintCompositionMap(
    int TotalTargetCount,
    int ReportedTargetCount,
    bool Truncated,
    IReadOnlyList<BlueprintCompositionTargetSummary> Targets);

internal sealed record BlueprintCompositionTargetSummary(
    string TargetPath,
    string ParentJsonPath,
    string ParentKind,
    string SlotName,
    int CurrentCount,
    int MinItems,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? MaxItems,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? RemainingCapacity);
