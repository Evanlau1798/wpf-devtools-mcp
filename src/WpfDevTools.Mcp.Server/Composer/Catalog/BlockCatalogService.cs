using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Catalog;

internal sealed class BlockCatalogService(PackRegistry registry)
{
    public BlockCatalogResult GetCatalog(BlockCatalogQuery query)
    {
        var registryResult = registry.ListPacks();
        var packIds = query.PackIds is { Count: > 0 }
            ? query.PackIds.ToHashSet(StringComparer.Ordinal)
            : null;
        var items = new List<BlockCatalogItem>();
        var resolvedComposableBlockKinds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pack in registryResult.Packs)
        {
            if (packIds is not null && !packIds.Contains(pack.Id))
            {
                continue;
            }

            var loadedPack = ComposerPackLoader.Load(pack.RootPath);
            foreach (var block in loadedPack.Blocks)
            {
                var item = CreateItem(pack, loadedPack.Manifest, block);
                if (item.RendererAvailable)
                {
                    resolvedComposableBlockKinds.Add(block.Kind);
                }

                if (Matches(query, item))
                {
                    items.Add(item);
                }
            }
        }

        return new BlockCatalogResult(
            items.OrderBy(item => item.Kind, StringComparer.Ordinal).ToArray(),
            resolvedComposableBlockKinds.OrderBy(kind => kind, StringComparer.Ordinal).ToArray(),
            registryResult.Diagnostics);
    }

    private static bool Matches(BlockCatalogQuery query, BlockCatalogItem item)
    {
        return (string.IsNullOrWhiteSpace(query.Kind) || string.Equals(item.Kind, query.Kind, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(query.Category) || string.Equals(item.Category, query.Category, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(query.KindPrefix) || item.Kind.StartsWith(query.KindPrefix, StringComparison.Ordinal))
            && (!query.ComposableOnly || item.RendererAvailable);
    }

    private static BlockCatalogItem CreateItem(
        PackRegistryItem pack,
        UiPackManifest manifest,
        UiBlockDefinition block)
    {
        var slots = block.Slots.ToDictionary(
            pair => pair.Key,
            pair => new BlockCatalogSlot(pair.Value.AllowedKinds),
            StringComparer.Ordinal);
        var properties = block.Properties.ToDictionary(
            pair => pair.Key,
            pair => new BlockCatalogProperty(pair.Value.Type, pair.Value.Required, pair.Value.Default),
            StringComparer.Ordinal);

        return new BlockCatalogItem(
            pack.Id,
            pack.Version,
            block.Kind,
            block.DisplayName,
            block.Category,
            properties,
            slots,
            slots.Values.SelectMany(slot => slot.AllowedKinds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            HasRenderer(manifest, block),
            block.SourceHints.Select(hint => hint.Path).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray());
    }

    private static bool HasRenderer(UiPackManifest manifest, UiBlockDefinition block)
    {
        if (string.IsNullOrWhiteSpace(block.Renderer.XamlTemplate)
            || Path.IsPathRooted(block.Renderer.XamlTemplate)
            || string.IsNullOrWhiteSpace(block.SourceFilePath))
        {
            return false;
        }

        var packRoot = new FileInfo(block.SourceFilePath).Directory?.Parent?.FullName;
        if (packRoot is null)
        {
            return false;
        }

        var rendererPath = Path.GetFullPath(
            Path.Combine(packRoot, block.Renderer.XamlTemplate.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(rendererPath) && manifest.Blocks.Contains(block.Kind, StringComparer.Ordinal);
    }
}

internal sealed record BlockCatalogQuery(
    IReadOnlyList<string>? PackIds = null,
    string? Category = null,
    string? KindPrefix = null,
    bool ComposableOnly = false,
    string? Kind = null);

internal sealed record BlockCatalogResult(
    IReadOnlyList<BlockCatalogItem> Items,
    IReadOnlyList<string> ResolvedComposableBlockKinds,
    IReadOnlyList<string> Diagnostics);

internal sealed record BlockCatalogItem(
    string PackId,
    string PackVersion,
    string Kind,
    string DisplayName,
    string Category,
    IReadOnlyDictionary<string, BlockCatalogProperty> Properties,
    IReadOnlyDictionary<string, BlockCatalogSlot> Slots,
    IReadOnlyList<string> AllowedKinds,
    bool RendererAvailable,
    IReadOnlyList<string> SourceHintSummary);

internal sealed record BlockCatalogProperty(string Type, bool Required, JsonElement? Default);

internal sealed record BlockCatalogSlot(IReadOnlyList<string> AllowedKinds);
