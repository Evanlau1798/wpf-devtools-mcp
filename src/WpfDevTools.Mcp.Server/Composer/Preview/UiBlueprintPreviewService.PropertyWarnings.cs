using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiBlueprintPreviewService
{
    private IReadOnlyList<PreviewPropertyWarning> CollectPropertyWarnings(string blueprintJson)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
            blueprintJson,
            "<inline-blueprint>",
            UiComposerSchemaVersions.UiBlueprint);
        var declared = blueprint.Packs.ToDictionary(pack => pack.Id, StringComparer.Ordinal);
        var blocks = new Dictionary<string, UiBlockDefinition>(StringComparer.Ordinal);
        foreach (var pack in registry.ListPacks().Packs.Where(pack =>
                     declared.TryGetValue(pack.Id, out var reference)
                     && string.Equals(pack.Version, reference.Version, StringComparison.Ordinal)))
        {
            foreach (var block in ComposerPackLoader.Load(pack.RootPath).Blocks)
            {
                blocks[block.Kind] = block;
            }
        }

        var warnings = new List<PreviewPropertyWarning>();
        CollectPropertyWarnings(blueprint.Layout, "$.layout", blocks, warnings);
        return warnings;
    }

    private static void CollectPropertyWarnings(
        UiBlueprintNode node,
        string path,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        List<PreviewPropertyWarning> warnings)
    {
        if (blocks.TryGetValue(node.Kind, out var block))
        {
            foreach (var propertyName in node.Properties.Keys)
            {
                if (block.Properties.TryGetValue(propertyName, out var property)
                    && !string.IsNullOrWhiteSpace(property.PreviewWarning))
                {
                    warnings.Add(new PreviewPropertyWarning(
                        AppendJsonPath(path + ".properties", propertyName),
                        node.Kind,
                        propertyName,
                        property.PreviewWarning));
                }
            }
        }

        foreach (var (slotName, children) in node.Slots)
        {
            var slotPath = AppendJsonPath(path + ".slots", slotName);
            for (var index = 0; index < children.Length; index++)
            {
                CollectPropertyWarnings(children[index], $"{slotPath}[{index}]", blocks, warnings);
            }
        }
    }

    private static string AppendJsonPath(string path, string propertyName)
        => IsSimpleJsonPathName(propertyName)
            ? $"{path}.{propertyName}"
            : $"{path}[{JsonSerializer.Serialize(propertyName)}]";

    private static bool IsSimpleJsonPathName(string value)
        => value.Length > 0
            && (char.IsLetter(value[0]) || value[0] == '_')
            && value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
}
