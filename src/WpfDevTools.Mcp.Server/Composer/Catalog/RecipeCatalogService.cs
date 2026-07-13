using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Catalog;

internal sealed class RecipeCatalogService(PackRegistry registry)
{
    public RecipeCatalogResult GetCatalog(RecipeCatalogQuery query)
    {
        var registryResult = registry.ListPacks();
        var packIds = query.PackIds is { Count: > 0 }
            ? query.PackIds.ToHashSet(StringComparer.Ordinal)
            : null;
        var items = new List<RecipeCatalogItem>();

        foreach (var pack in registryResult.Packs)
        {
            if (packIds is not null && !packIds.Contains(pack.Id))
            {
                continue;
            }

            var loadedPack = ComposerPackLoader.Load(pack.RootPath);
            foreach (var recipe in loadedPack.Recipes)
            {
                if (!string.IsNullOrWhiteSpace(query.RecipeId)
                    && !string.Equals(recipe.Id, query.RecipeId, StringComparison.Ordinal))
                {
                    continue;
                }

                items.Add(CreateItem(pack, recipe));
            }
        }

        return new RecipeCatalogResult(
            items.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            registryResult.Diagnostics);
    }

    private static RecipeCatalogItem CreateItem(PackRegistryItem pack, UiRecipeDefinition recipe)
    {
        var inputs = recipe.Inputs.ToDictionary(
            pair => pair.Key,
            pair => new RecipeCatalogInput(
                pair.Value.Type,
                pair.Value.Required,
                pair.Value.Default,
                pair.Value.Description,
                GetAllowedValues(pair.Value)),
            StringComparer.Ordinal);

        return new RecipeCatalogItem(
            pack.Id,
            pack.Version,
            recipe.Id,
            recipe.DisplayName,
            recipe.Description,
            recipe.RequiredPacks,
            inputs,
            TryGetRootKind(recipe.ExpandsTo),
            recipe.CustomizationGuidance);
    }

    private static string[] GetAllowedValues(UiRecipeInput input)
        => input.AllowedValues.Length > 0 ? input.AllowedValues : input.EnumValues;

    private static string TryGetRootKind(JsonElement expandsTo)
        => expandsTo.ValueKind == JsonValueKind.Object
            && expandsTo.TryGetProperty("kind", out var kind)
            ? kind.GetString() ?? string.Empty
            : string.Empty;
}

internal sealed record RecipeCatalogQuery(
    IReadOnlyList<string>? PackIds = null,
    string? RecipeId = null);

internal sealed record RecipeCatalogResult(
    IReadOnlyList<RecipeCatalogItem> Items,
    IReadOnlyList<string> Diagnostics);

internal sealed record RecipeCatalogItem(
    string PackId,
    string PackVersion,
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<ComposerPackReference> RequiredPacks,
    IReadOnlyDictionary<string, RecipeCatalogInput> Inputs,
    string RootKind,
    IReadOnlyList<string> CustomizationGuidance);

internal sealed record RecipeCatalogInput(
    string Type,
    bool Required,
    JsonElement? Default,
    string Description,
    IReadOnlyList<string> AllowedValues);
