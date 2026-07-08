using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintPackConflictDiagnostics
{
    public static IReadOnlyList<BlueprintValidationIssue> FindResourceConflicts(
        IReadOnlyDictionary<string, PackRegistryItem> declaredPacks)
    {
        var ownerByResource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<BlueprintValidationIssue>();

        foreach (var pack in declaredPacks.Values.OrderBy(pack => pack.Id, StringComparer.Ordinal))
        {
            ComposerPack loaded;
            try
            {
                loaded = ComposerPackLoader.Load(pack.RootPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                warnings.Add(new(
                    "$.packs",
                    "PackResourceInspectionFailed",
                    $"Pack '{pack.Id}' resources could not be inspected.",
                    "Repair or reinstall the pack before relying on resource conflict diagnostics.",
                    [],
                    [],
                    null));
                continue;
            }

            foreach (var resource in loaded.Manifest.ResourceSetup.ApplicationMergedDictionaries)
            {
                if (string.IsNullOrWhiteSpace(resource))
                {
                    continue;
                }

                if (ownerByResource.TryGetValue(resource, out var owner)
                    && !string.Equals(owner, pack.Id, StringComparison.Ordinal))
                {
                    warnings.Add(new(
                        "$.packs",
                        "PackResourceConflict",
                        $"Resource dictionary '{resource}' is declared by packs '{owner}' and '{pack.Id}'.",
                        "Keep a single owner for shared theme resources or split optional pack resources into a distinct dictionary.",
                        [],
                        [],
                        null));
                    continue;
                }

                ownerByResource[resource] = pack.Id;
            }
        }

        return warnings;
    }
}
