using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Catalog;

internal sealed class BlockCatalogService(PackRegistry registry)
{
    private const int MaxAllowedValues = 12;

    public BlockCatalogResult GetCatalog(BlockCatalogQuery query)
    {
        var registryResult = registry.ListPacks();
        var packIds = query.PackIds is { Count: > 0 }
            ? query.PackIds.ToHashSet(StringComparer.Ordinal)
            : null;
        var items = new List<BlockCatalogItem>();

        foreach (var pack in registryResult.Packs)
        {
            if (packIds is not null && !packIds.Contains(pack.Id))
            {
                continue;
            }

            var loadedPack = ComposerPackLoader.Load(pack.RootPath);
            foreach (var block in loadedPack.Blocks)
            {
                var item = CreateItem(
                    pack,
                    loadedPack.Manifest,
                    block,
                    query.AllowedValueQuery);
                if (Matches(query, item))
                {
                    items.Add(item);
                }
            }
        }

        return new BlockCatalogResult(
            items.OrderBy(item => item.Kind, StringComparer.Ordinal).ToArray(),
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
        UiBlockDefinition block,
        string? allowedValueQuery)
    {
        var slots = block.Slots.ToDictionary(
            pair => pair.Key,
            pair => new BlockCatalogSlot(
                pair.Value.Description,
                pair.Value.AllowedKinds,
                pair.Value.MinItems,
                pair.Value.MaxItems),
            StringComparer.Ordinal);
        var properties = block.Properties.ToDictionary(
            pair => pair.Key,
            pair => CreateProperty(pair.Value, allowedValueQuery),
            StringComparer.Ordinal);

        return new BlockCatalogItem(
            pack.Id,
            pack.Version,
            block.Kind,
            block.DisplayName,
            block.Description,
            block.Category,
            block.AuthoringRoles,
            properties,
            slots,
            slots.Values.SelectMany(slot => slot.AllowedKinds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            HasRenderer(manifest, block),
            CreateCompositionSkeleton(block),
            block.SourceHints.Select(hint => hint.Path).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray());
    }

    private static BlockCatalogProperty CreateProperty(UiBlockProperty property, string? allowedValueQuery)
    {
        var allAllowedValues = property.AllowedValues.Length > 0 ? property.AllowedValues : property.EnumValues;
        var query = string.IsNullOrWhiteSpace(allowedValueQuery) ? null : allowedValueQuery.Trim();
        var matches = query is null
            ? allAllowedValues
            : allAllowedValues.Where(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var allowedValues = matches.Length <= MaxAllowedValues ? matches : matches[..MaxAllowedValues];
        return new BlockCatalogProperty(
            property.Type,
            property.Description,
            property.PreviewWarning,
            property.Required,
            property.Default,
            allowedValues,
            allAllowedValues.Length,
            matches.Length,
            allowedValues.Length < matches.Length,
            property.Minimum,
            property.Maximum,
            property.Integer,
            property.Format);
    }

    private static JsonElement? CreateCompositionSkeleton(UiBlockDefinition block)
    {
        var node = new Dictionary<string, object?> { ["kind"] = block.Kind };
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, property) in block.Properties.Where(pair => pair.Value.Required))
        {
            if (!TryGetSkeletonValue(property, out var value))
            {
                return null;
            }

            properties[name] = value;
        }

        if (properties.Count > 0)
        {
            node["properties"] = properties;
        }

        if (block.Slots.Count > 0)
        {
            node["slots"] = block.Slots.ToDictionary(
                pair => pair.Key,
                _ => Array.Empty<object>(),
                StringComparer.Ordinal);
        }

        return JsonSerializer.SerializeToElement(node);
    }

    private static bool TryGetSkeletonValue(UiBlockProperty property, out object? value)
    {
        if (property.Default.HasValue && UiBlockPropertyValueRules.IsValid(property.Default.Value, property))
        {
            value = property.Default.Value;
            return true;
        }

        var allowedValues = property.AllowedValues.Length > 0 ? property.AllowedValues : property.EnumValues;
        foreach (var allowedValue in allowedValues)
        {
            if (TryCandidate(allowedValue, property, out value))
            {
                return true;
            }
        }

        var candidate = property.Type switch
        {
            "boolean" or "bool" => false,
            "binding" => "{Binding}",
            "string" when property.Format == "thickness" => "0",
            "string" when property.Format == "gridLength" => "Auto",
            "string" => "Value",
            "number" => GetNumberCandidate(property),
            "object" => new Dictionary<string, object?>(),
            _ => null
        };
        return TryCandidate(candidate, property, out value);
    }

    private static object GetNumberCandidate(UiBlockProperty property)
    {
        var candidate = 0d;
        if (property.Minimum is double minimum && candidate < minimum)
        {
            candidate = minimum;
        }
        if (property.Maximum is double maximum && candidate > maximum)
        {
            candidate = maximum;
        }

        return property.Integer
            ? property.Minimum.HasValue ? Math.Ceiling(candidate) : Math.Floor(candidate)
            : candidate;
    }

    private static bool TryCandidate(object? candidate, UiBlockProperty property, out object? value)
    {
        var element = JsonSerializer.SerializeToElement(candidate);
        value = candidate;
        return UiBlockPropertyValueRules.IsValid(element, property);
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
    string? Kind = null,
    string? AllowedValueQuery = null);

internal sealed record BlockCatalogResult(
    IReadOnlyList<BlockCatalogItem> Items,
    IReadOnlyList<string> Diagnostics);

internal sealed record BlockCatalogItem(
    string PackId,
    string PackVersion,
    string Kind,
    string DisplayName,
    string Description,
    string Category,
    IReadOnlyList<string> AuthoringRoles,
    IReadOnlyDictionary<string, BlockCatalogProperty> Properties,
    IReadOnlyDictionary<string, BlockCatalogSlot> Slots,
    IReadOnlyList<string> AllowedKinds,
    bool RendererAvailable,
    JsonElement? CompositionSkeleton,
    IReadOnlyList<string> SourceHintSummary);

internal sealed record BlockCatalogProperty(
    string Type,
    string Description,
    string PreviewWarning,
    bool Required,
    JsonElement? Default,
    IReadOnlyList<string> AllowedValues,
    int AllowedValueCount,
    int AllowedValueMatchCount,
    bool AllowedValuesTruncated,
    double? Minimum,
    double? Maximum,
    bool Integer,
    string Format);

internal sealed record BlockCatalogSlot(
    string Description,
    IReadOnlyList<string> AllowedKinds,
    int MinItems,
    int? MaxItems);
