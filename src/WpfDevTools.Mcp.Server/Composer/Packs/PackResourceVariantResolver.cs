using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class PackResourceVariantResolver
{
    private static readonly HashSet<string> AllowedAppearances = new(StringComparer.Ordinal)
    {
        "dark",
        "light",
        "neutral"
    };

    public static ResolvedPackResourceVariant Resolve(UiPackManifest manifest, string? selectedVariant)
    {
        var setup = manifest.ResourceSetup;
        if (setup.Variants.Count == 0)
        {
            return new(string.Empty, "neutral", setup.ApplicationMergedDictionaries);
        }

        var variantId = string.IsNullOrWhiteSpace(selectedVariant)
            ? setup.DefaultVariant
            : selectedVariant;
        return setup.Variants.TryGetValue(variantId, out var variant)
            ? new(variantId, variant.Appearance, variant.ApplicationMergedDictionaries)
            : new(variantId, string.Empty, []);
    }

    public static PackResourceVariantCatalog Describe(UiPackManifest manifest)
        => new(
            manifest.ResourceSetup.DefaultVariant,
            manifest.ResourceSetup.Variants
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new PackResourceVariantDescriptor(item.Key, item.Value.Appearance))
                .ToArray());

    public static void Validate(UiPackManifest manifest)
    {
        var setup = manifest.ResourceSetup;
        if (setup.Variants.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(setup.DefaultVariant)
            || !setup.Variants.ContainsKey(setup.DefaultVariant))
        {
            throw new InvalidDataException(
                $"Pack '{manifest.Id}' resourceSetup.defaultVariant must name a declared resource variant.");
        }

        foreach (var (id, variant) in setup.Variants)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException($"Pack '{manifest.Id}' contains an empty resource variant id.");
            }

            if (!AllowedAppearances.Contains(variant.Appearance))
            {
                throw new InvalidDataException(
                    $"Pack '{manifest.Id}' resource variant '{id}' appearance must be dark, light, or neutral.");
            }
        }
    }
}

internal sealed record ResolvedPackResourceVariant(
    string Id,
    string Appearance,
    IReadOnlyList<string> ApplicationMergedDictionaries);

internal sealed record PackResourceVariantCatalog(
    string DefaultVariant,
    IReadOnlyList<PackResourceVariantDescriptor> Variants);

internal sealed record PackResourceVariantDescriptor(string Id, string Appearance);
