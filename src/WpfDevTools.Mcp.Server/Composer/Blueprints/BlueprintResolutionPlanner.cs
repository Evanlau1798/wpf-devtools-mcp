using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintResolutionPlanner
{
    public static BlueprintResolutionPlan Build(
        UiBlueprint blueprint,
        IReadOnlyList<PackRegistryItem> availablePacks)
    {
        var availableById = availablePacks.ToDictionary(pack => pack.Id, StringComparer.Ordinal);
        var packs = new List<ResolvedPackPlan>();
        var resources = new List<string>();
        var resourceOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<BlueprintPackConflictPlan>();
        var visualPackIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var packRef in blueprint.Packs)
        {
            if (!availableById.TryGetValue(packRef.Id, out var available))
            {
                packs.Add(new(
                    packRef.Id,
                    packRef.Version,
                    string.Empty,
                    packRef.Role,
                    string.Empty,
                    packRef.Required,
                    string.Empty,
                    string.Empty,
                    packRef.Required ? "missing-required" : "missing-optional"));
                continue;
            }

            var versionMatches = string.Equals(available.Version, packRef.Version, StringComparison.Ordinal);
            packs.Add(new(
                packRef.Id,
                packRef.Version,
                available.Version,
                packRef.Role,
                available.Role,
                packRef.Required,
                ToScopeName(available.Scope),
                available.Kind,
                versionMatches ? "resolved" : "version-mismatch"));
            if (!versionMatches)
            {
                continue;
            }

            if (available.Kind is "style-pack" or "skill-generated-style-pack")
            {
                visualPackIds.Add(available.Id);
            }

            ComposerPack loaded;
            try
            {
                loaded = ComposerPackLoader.Load(available.RootPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                conflicts.Add(new(
                    "PackResourceInspectionFailed",
                    "warning",
                    [available.Id],
                    null,
                    $"Pack '{available.Id}' resources could not be inspected.",
                    "Repair or reinstall the pack before relying on the resolved resource plan."));
                continue;
            }

            foreach (var resource in loaded.Manifest.ResourceSetup.ApplicationMergedDictionaries)
            {
                if (string.IsNullOrWhiteSpace(resource))
                {
                    continue;
                }

                if (resourceOwners.TryGetValue(resource, out var owner))
                {
                    if (!string.Equals(owner, available.Id, StringComparison.Ordinal))
                    {
                        conflicts.Add(new(
                            "PackResourceConflict",
                            "warning",
                            [owner, available.Id],
                            resource,
                            $"Resource dictionary '{resource}' is declared by packs '{owner}' and '{available.Id}'.",
                            "Keep a single owner for shared resources or split optional resources into a distinct dictionary."));
                    }

                    continue;
                }

                resourceOwners[resource] = available.Id;
                resources.Add(resource);
            }
        }

        if (visualPackIds.Count > 1)
        {
            var ids = visualPackIds.Order(StringComparer.Ordinal).ToArray();
            conflicts.Add(new(
                "MultipleVisualPacks",
                "error",
                ids,
                null,
                $"Multiple complete visual packs are declared: {string.Join(", ", ids)}.",
                "Keep one visual style pack as primary and use focused control, icon, layout, recipe, or extension packs for additional capabilities."));
        }

        return new BlueprintResolutionPlan(packs, resources, conflicts);
    }

    private static string ToScopeName(PackScope scope)
        => scope switch
        {
            PackScope.ProjectLocal => "project-local",
            PackScope.UserGlobal => "user-global",
            PackScope.Builtin => "built-in",
            _ => "unknown"
        };
}
