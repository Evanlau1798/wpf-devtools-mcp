using System.Globalization;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintVisualThemeDiagnostics
{
    private const int MaxWarnings = 16;

    public static void AddIssues(
        UiBlueprint blueprint,
        IReadOnlyList<PackRegistryItem> availablePacks,
        List<BlueprintValidationIssue> errors,
        List<BlueprintValidationIssue> warnings)
    {
        var declaredIds = blueprint.Packs.Select(pack => pack.Id).ToHashSet(StringComparer.Ordinal);
        var loaded = LoadDeclaredPacks(blueprint, availablePacks);
        ValidateSelections(blueprint, declaredIds, loaded, errors);

        var blocks = loaded.Values
            .SelectMany(pack => pack.Blocks)
            .ToDictionary(block => block.Kind, StringComparer.Ordinal);
        foreach (var packRef in blueprint.Packs)
        {
            if (!loaded.TryGetValue(packRef.Id, out var pack))
            {
                continue;
            }

            blueprint.ResourceVariants.TryGetValue(packRef.Id, out var selectedId);
            var selected = PackResourceVariantResolver.Resolve(pack.Manifest, selectedId);
            if (selected.Appearance is not "dark" and not "light")
            {
                continue;
            }

            AddSurfaceWarnings(
                blueprint.Layout,
                "$.layout",
                packRef.Id,
                selected,
                blocks,
                warnings);
            if (warnings.Count >= MaxWarnings)
            {
                return;
            }
        }
    }

    private static Dictionary<string, ComposerPack> LoadDeclaredPacks(
        UiBlueprint blueprint,
        IReadOnlyList<PackRegistryItem> availablePacks)
    {
        var requested = new Dictionary<string, ComposerPackReference>(StringComparer.Ordinal);
        foreach (var pack in blueprint.Packs)
        {
            requested[pack.Id] = pack;
        }
        var loaded = new Dictionary<string, ComposerPack>(StringComparer.Ordinal);
        foreach (var available in availablePacks)
        {
            if (!requested.TryGetValue(available.Id, out var packRef)
                || !string.Equals(packRef.Version, available.Version, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                loaded[available.Id] = ComposerPackLoader.Load(available.RootPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                // Pack loading diagnostics are already emitted by the registry and normal validation flow.
            }
        }

        return loaded;
    }

    private static void ValidateSelections(
        UiBlueprint blueprint,
        IReadOnlySet<string> declaredIds,
        IReadOnlyDictionary<string, ComposerPack> loaded,
        List<BlueprintValidationIssue> errors)
    {
        foreach (var (packId, variantId) in blueprint.ResourceVariants)
        {
            var path = $"$.resourceVariants.{packId}";
            if (!declaredIds.Contains(packId))
            {
                errors.Add(Issue(
                    path,
                    "ResourceVariantPackNotDeclared",
                    $"Resource variant selection references undeclared pack '{packId}'.",
                    "Declare the pack in packs[] or remove its resourceVariants entry."));
                continue;
            }

            if (!loaded.TryGetValue(packId, out var pack))
            {
                continue;
            }

            var allowed = pack.Manifest.ResourceSetup.Variants.Keys.Order(StringComparer.Ordinal).ToArray();
            if (!pack.Manifest.ResourceSetup.Variants.ContainsKey(variantId))
            {
                errors.Add(Issue(
                    path,
                    "UnknownResourceVariant",
                    $"Pack '{packId}' does not declare resource variant '{variantId}'.",
                    "Choose a resource variant returned by list_ui_block_packs.",
                    allowed));
            }
        }
    }

    private static void AddSurfaceWarnings(
        UiBlueprintNode node,
        string path,
        string themedPackId,
        ResolvedPackResourceVariant selected,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        List<BlueprintValidationIssue> warnings)
    {
        if (warnings.Count >= MaxWarnings)
        {
            return;
        }

        if (blocks.TryGetValue(node.Kind, out var block)
            && ContainsPack(node, themedPackId))
        {
            foreach (var (propertyName, property) in block.Properties)
            {
                if (!string.Equals(property.VisualRole, "surface", StringComparison.Ordinal)
                    || !node.Properties.TryGetValue(propertyName, out var value)
                    || !TryGetAppearance(value, out var surfaceAppearance)
                    || string.Equals(surfaceAppearance, selected.Appearance, StringComparison.Ordinal))
                {
                    continue;
                }

                warnings.Add(Issue(
                    $"{path}.properties.{propertyName}",
                    "SurfaceThemeContrastRisk",
                    $"Explicit {surfaceAppearance} surface conflicts with resource variant '{selected.Id}' ({selected.Appearance}) for theme-styled pack '{themedPackId}' in this subtree.",
                    "Choose a compatible resource variant or keep the surface theme-neutral so pack-owned foregrounds remain readable."));
                break;
            }
        }

        foreach (var (slotName, children) in node.Slots)
        {
            for (var index = 0; index < children.Length; index++)
            {
                AddSurfaceWarnings(
                    children[index],
                    $"{path}.slots.{slotName}[{index}]",
                    themedPackId,
                    selected,
                    blocks,
                    warnings);
            }
        }
    }

    private static bool ContainsPack(UiBlueprintNode root, string packId)
    {
        var prefix = packId + ".";
        var stack = new Stack<UiBlueprintNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Kind.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var child in node.Slots.Values.SelectMany(children => children))
            {
                stack.Push(child);
            }
        }

        return false;
    }

    private static bool TryGetAppearance(JsonElement value, out string appearance)
    {
        appearance = string.Empty;
        if (value.ValueKind != JsonValueKind.String
            || !TryParseHexColor(value.GetString(), out var red, out var green, out var blue))
        {
            return false;
        }

        var luminance = 0.2126 * ToLinear(red) + 0.7152 * ToLinear(green) + 0.0722 * ToLinear(blue);
        appearance = luminance switch
        {
            >= 0.55 => "light",
            <= 0.20 => "dark",
            _ => string.Empty
        };
        return appearance.Length > 0;
    }

    private static bool TryParseHexColor(string? text, out byte red, out byte green, out byte blue)
    {
        red = green = blue = 0;
        if (string.IsNullOrWhiteSpace(text) || text[0] != '#')
        {
            return false;
        }

        var hex = text[1..];
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }
        else if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(character => new string(character, 2)));
        }

        return hex.Length == 6
            && byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red)
            && byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green)
            && byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue);
    }

    private static double ToLinear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static BlueprintValidationIssue Issue(
        string path,
        string code,
        string message,
        string repair,
        IReadOnlyList<string>? allowedValues = null)
        => new(path, code, message, repair, [], allowedValues ?? [], null);
}
