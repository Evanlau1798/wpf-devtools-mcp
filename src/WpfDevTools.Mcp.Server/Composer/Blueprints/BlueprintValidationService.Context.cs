using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed partial class BlueprintValidationService
{
    private static BlueprintValidationContext BuildContext(
        IReadOnlyDictionary<string, PackRegistryItem> declaredPacks,
        IReadOnlySet<string> declaredPackIds,
        IReadOnlySet<string> optionalMissingPackIds,
        List<BlueprintValidationIssue> errors)
    {
        var blocks = new Dictionary<string, UiBlockDefinition>(StringComparer.Ordinal);
        var packKinds = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var packFingerprints = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pack in declaredPacks.Values)
        {
            try
            {
                var loadedResult = ComposerPackLoader.LoadWithFingerprint(pack.RootPath);
                var loaded = loadedResult.Pack;
                packFingerprints[pack.Id] = loadedResult.Fingerprint;
                packKinds[pack.Id] = loaded.Blocks.Select(block => block.Kind).Order(StringComparer.Ordinal).ToArray();
                foreach (var block in loaded.Blocks)
                {
                    blocks[block.Kind] = block;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                errors.Add(Issue("$.packs", "PackLoadFailed", $"Pack '{pack.Id}' could not be loaded: {ex.Message}", "Repair or reinstall the pack, then retry validation."));
            }
        }

        return new BlueprintValidationContext(
            declaredPackIds,
            declaredPacks.Keys.ToHashSet(StringComparer.Ordinal),
            optionalMissingPackIds,
            blocks,
            packKinds)
        {
            PackFingerprints = packFingerprints
        };
    }
}
