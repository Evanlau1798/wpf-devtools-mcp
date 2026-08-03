using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiBlueprintPreviewService
{
    private string ResolveRootRendererTemplatePath(string blueprintJson)
    {
        try
        {
            var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
                blueprintJson,
                "<inline-blueprint>",
                UiComposerSchemaVersions.UiBlueprint);
            var packId = ComposerPackKindResolver.ResolveDeclaredPackId(
                             blueprint.Layout.Kind,
                             blueprint.Packs.Select(pack => pack.Id))
                         ?? ComposerPackKindResolver.GetFallbackPackId(blueprint.Layout.Kind);
            var packRef = blueprint.Packs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, packId, StringComparison.Ordinal));
            if (packRef is null)
            {
                return string.Empty;
            }

            var pack = registry.ListPacks().Packs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, packRef.Id, StringComparison.Ordinal)
                && string.Equals(candidate.Version, packRef.Version, StringComparison.Ordinal));
            if (pack is null)
            {
                return string.Empty;
            }

            var block = ComposerPackLoader.Load(pack.RootPath).Blocks.FirstOrDefault(candidate =>
                string.Equals(candidate.Kind, blueprint.Layout.Kind, StringComparison.Ordinal));
            return block is null || string.IsNullOrWhiteSpace(block.Renderer.XamlTemplate)
                ? string.Empty
                : Path.GetFullPath(Path.Combine(
                    pack.RootPath,
                    block.Renderer.XamlTemplate.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch
        {
            return string.Empty;
        }
    }

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
