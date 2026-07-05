using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed class BlueprintValidationService(PackRegistry registry)
{
    private static readonly HashSet<string> PrimitiveKinds = new(StringComparer.Ordinal)
    {
        "stack",
        "template",
        "text"
    };

    public BlueprintValidationResult Validate(string blueprintJson)
    {
        var errors = new List<BlueprintValidationIssue>();
        var warnings = new List<BlueprintValidationIssue>();
        UiBlueprint blueprint;

        try
        {
            blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
                blueprintJson,
                "<inline-blueprint>",
                UiComposerSchemaVersions.UiBlueprint);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            errors.Add(Issue("$", "InvalidBlueprintJson", ex.Message, "Provide valid JSON with schemaVersion wpfdevtools.ui-blueprint.v1."));
            return new BlueprintValidationResult(errors, warnings, []);
        }

        var registryResult = registry.ListPacks();
        var declaredPacks = ValidatePacks(blueprint, registryResult.Packs, errors);
        ValidateRequiredFields(blueprint, errors);

        if (!string.IsNullOrWhiteSpace(blueprint.PrimaryPack)
            && !declaredPacks.ContainsKey(blueprint.PrimaryPack))
        {
            errors.Add(Issue(
                "$.primaryPack",
                "PrimaryPackNotDeclared",
                $"Primary pack '{blueprint.PrimaryPack}' is not listed in packs.",
                "Add the primaryPack id to packs[] or choose a declared pack id."));
        }

        if (!string.IsNullOrWhiteSpace(blueprint.Layout.Kind))
        {
            var context = BuildContext(declaredPacks, errors);
            ValidateNode(blueprint.Layout, "$.layout", null, null, context, errors, warnings);
        }

        return new BlueprintValidationResult(errors, warnings, registryResult.Diagnostics);
    }

    private static Dictionary<string, PackRegistryItem> ValidatePacks(
        UiBlueprint blueprint,
        IReadOnlyList<PackRegistryItem> availablePacks,
        List<BlueprintValidationIssue> errors)
    {
        var availableById = availablePacks.ToDictionary(pack => pack.Id, StringComparer.Ordinal);
        var declared = new Dictionary<string, PackRegistryItem>(StringComparer.Ordinal);

        for (var index = 0; index < blueprint.Packs.Length; index++)
        {
            var packRef = blueprint.Packs[index];
            var path = $"$.packs[{index}]";
            if (string.IsNullOrWhiteSpace(packRef.Id))
            {
                errors.Add(Issue(path + ".id", "PackIdMissing", "Pack reference is missing id.", "Set packs[].id to an installed pack id such as wpfui."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(packRef.Version))
            {
                errors.Add(Issue(path + ".version", "ExplicitPackVersionRequired", $"Pack '{packRef.Id}' is missing an explicit version.", "Set packs[].version to the installed pack version."));
                continue;
            }

            if (!availableById.TryGetValue(packRef.Id, out var available))
            {
                errors.Add(Issue(path, "PackNotFound", $"Pack '{packRef.Id}' {packRef.Version} is not installed.", "Install or reference a Composer pack before validating this blueprint."));
                continue;
            }

            if (!string.Equals(available.Version, packRef.Version, StringComparison.Ordinal))
            {
                errors.Add(Issue(path + ".version", "PackVersionMismatch", $"Pack '{packRef.Id}' requested version {packRef.Version}, but installed version is {available.Version}.", $"Use version '{available.Version}' or install version '{packRef.Version}'."));
                continue;
            }

            declared[packRef.Id] = available;
        }

        return declared;
    }

    private static void ValidateRequiredFields(UiBlueprint blueprint, List<BlueprintValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(blueprint.Name))
        {
            errors.Add(Issue("$.name", "RequiredFieldMissing", "Blueprint name is required.", "Set name to a stable blueprint identifier."));
        }

        if (blueprint.Packs.Length == 0)
        {
            errors.Add(Issue("$.packs", "RequiredFieldMissing", "Blueprint packs[] must contain at least one pack reference.", "Add the primary pack reference with id and version."));
        }

        if (string.IsNullOrWhiteSpace(blueprint.PrimaryPack))
        {
            errors.Add(Issue("$.primaryPack", "RequiredFieldMissing", "Blueprint primaryPack is required.", "Set primaryPack to one of the declared pack ids."));
        }

        if (string.IsNullOrWhiteSpace(blueprint.Layout.Kind))
        {
            errors.Add(Issue("$.layout.kind", "RequiredFieldMissing", "Blueprint layout.kind is required.", "Set layout.kind to a pack-qualified block kind such as wpfui.card."));
        }
    }

    private static BlueprintValidationContext BuildContext(
        IReadOnlyDictionary<string, PackRegistryItem> declaredPacks,
        List<BlueprintValidationIssue> errors)
    {
        var blocks = new Dictionary<string, UiBlockDefinition>(StringComparer.Ordinal);
        var packKinds = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var pack in declaredPacks.Values)
        {
            try
            {
                var loaded = ComposerPackLoader.Load(pack.RootPath);
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

        return new BlueprintValidationContext(declaredPacks.Keys.ToHashSet(StringComparer.Ordinal), blocks, packKinds);
    }

    private static void ValidateNode(
        UiBlueprintNode node,
        string path,
        string? parentSlot,
        IReadOnlyList<string>? allowedKinds,
        BlueprintValidationContext context,
        List<BlueprintValidationIssue> errors,
        List<BlueprintValidationIssue> warnings)
    {
        if (string.IsNullOrWhiteSpace(node.Kind))
        {
            errors.Add(Issue(path + ".kind", "RequiredFieldMissing", "Block kind is required.", "Set kind to a pack-qualified block kind."));
            return;
        }

        if (PrimitiveKinds.Contains(node.Kind))
        {
            ValidatePrimitiveNode(node, path, parentSlot, allowedKinds, errors);
            return;
        }

        if (!node.Kind.Contains('.', StringComparison.Ordinal))
        {
            errors.Add(Issue(path, "UnqualifiedBlockKind", $"Block kind '{node.Kind}' is not pack-qualified.", "Use a pack-qualified kind such as wpfui.button."));
            return;
        }

        var packId = node.Kind[..node.Kind.IndexOf('.', StringComparison.Ordinal)];
        if (!context.DeclaredPackIds.Contains(packId))
        {
            errors.Add(Issue(path, "PackNotDeclared", $"Block kind '{node.Kind}' uses undeclared pack '{packId}'.", $"Add pack '{packId}' with an explicit version to packs[] or choose a declared pack block."));
            return;
        }

        if (!context.Blocks.TryGetValue(node.Kind, out var block))
        {
            errors.Add(Issue(path, "UnknownBlockKind", $"Block kind '{node.Kind}' was not found in pack '{packId}'.", BuildUnknownKindSuggestion(context, packId)));
            return;
        }

        if (allowedKinds is not null && !allowedKinds.Contains(node.Kind, StringComparer.Ordinal))
        {
            errors.Add(Issue(
                path,
                "SlotChildKindNotAllowed",
                $"Block kind '{node.Kind}' is not allowed in slot '{parentSlot}'.",
                $"Use one of the allowedKinds for slot '{parentSlot}': {string.Join(", ", allowedKinds)}.",
                allowedKinds,
                parentSlot: parentSlot));
            return;
        }

        ValidateProperties(node, path, block, errors, warnings);
        ValidateSlots(node, path, block, context, errors, warnings);
    }

    private static void ValidatePrimitiveNode(
        UiBlueprintNode node,
        string path,
        string? parentSlot,
        IReadOnlyList<string>? allowedKinds,
        List<BlueprintValidationIssue> errors)
    {
        if (allowedKinds is null || !allowedKinds.Contains(node.Kind, StringComparer.Ordinal))
        {
            errors.Add(Issue(
                path,
                allowedKinds is null ? "UnqualifiedBlockKind" : "SlotChildKindNotAllowed",
                $"Primitive kind '{node.Kind}' is only valid when a parent slot allows it.",
                allowedKinds is null
                    ? "Use a pack-qualified root block kind."
                    : $"Use one of the allowedKinds for slot '{parentSlot}': {string.Join(", ", allowedKinds)}.",
                allowedKinds ?? [],
                parentSlot: parentSlot));
        }
    }

    private static void ValidateProperties(
        UiBlueprintNode node,
        string path,
        UiBlockDefinition block,
        List<BlueprintValidationIssue> errors,
        List<BlueprintValidationIssue> warnings)
    {
        foreach (var (name, property) in block.Properties)
        {
            if (property.Required && !node.Properties.ContainsKey(name))
            {
                errors.Add(Issue(
                    $"{path}.properties.{name}",
                    "RequiredPropertyMissing",
                    $"Block '{block.Kind}' requires property '{name}'.",
                    $"Add property '{name}' with type '{property.Type}'."));
            }
        }

        foreach (var (name, value) in node.Properties)
        {
            if (!block.Properties.TryGetValue(name, out var property))
            {
                warnings.Add(Issue($"{path}.properties.{name}", "UnknownProperty", $"Block '{block.Kind}' does not define property '{name}'.", $"Remove property '{name}' or update the pack contract."));
                continue;
            }

            if (!MatchesPropertyType(value, property.Type))
            {
                errors.Add(Issue($"{path}.properties.{name}", "PropertyTypeMismatch", $"Property '{name}' must be type '{property.Type}'.", $"Set property '{name}' to a JSON value compatible with '{property.Type}'."));
                continue;
            }

            var allowedValues = GetAllowedValues(property);
            if (allowedValues.Length > 0
                && value.ValueKind == JsonValueKind.String
                && !allowedValues.Contains(value.GetString() ?? string.Empty, StringComparer.Ordinal))
            {
                errors.Add(Issue($"{path}.properties.{name}", "PropertyValueNotAllowed", $"Property '{name}' value '{value.GetString()}' is not allowed.", $"Use one of: {string.Join(", ", allowedValues)}.", allowedValues: allowedValues));
            }
        }
    }

    private static void ValidateSlots(
        UiBlueprintNode node,
        string path,
        UiBlockDefinition block,
        BlueprintValidationContext context,
        List<BlueprintValidationIssue> errors,
        List<BlueprintValidationIssue> warnings)
    {
        foreach (var (slotName, children) in node.Slots)
        {
            if (!block.Slots.TryGetValue(slotName, out var slot))
            {
                errors.Add(Issue($"{path}.slots.{slotName}", "UnknownSlot", $"Block '{block.Kind}' does not define slot '{slotName}'.", $"Use one of the block slots: {string.Join(", ", block.Slots.Keys.Order(StringComparer.Ordinal))}."));
                continue;
            }

            for (var index = 0; index < children.Length; index++)
            {
                ValidateNode(children[index], $"{path}.slots.{slotName}[{index}]", slotName, slot.AllowedKinds, context, errors, warnings);
            }
        }
    }

    private static bool MatchesPropertyType(JsonElement value, string type)
        => type switch
        {
            "binding" or "string" => value.ValueKind == JsonValueKind.String,
            "boolean" or "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => value.ValueKind == JsonValueKind.Number,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => true
        };

    private static string[] GetAllowedValues(UiBlockProperty property)
        => property.AllowedValues.Length > 0 ? property.AllowedValues : property.EnumValues;

    private static string BuildUnknownKindSuggestion(BlueprintValidationContext context, string packId)
        => context.PackKinds.TryGetValue(packId, out var kinds) && kinds.Length > 0
            ? $"Choose one of the known block kinds for pack '{packId}': {string.Join(", ", kinds.Take(8))}."
            : $"Refresh the catalog for pack '{packId}' and choose a listed block kind.";

    private static BlueprintValidationIssue Issue(
        string jsonPath,
        string code,
        string message,
        string repairSuggestion,
        IReadOnlyList<string>? allowedKinds = null,
        IReadOnlyList<string>? allowedValues = null,
        string? parentSlot = null)
        => new(jsonPath, code, message, repairSuggestion, allowedKinds ?? [], allowedValues ?? [], parentSlot);
}

internal sealed record BlueprintValidationResult(
    IReadOnlyList<BlueprintValidationIssue> Errors,
    IReadOnlyList<BlueprintValidationIssue> Warnings,
    IReadOnlyList<string> Diagnostics)
{
    public bool Success => Errors.Count == 0;
}

internal sealed record BlueprintValidationIssue(
    string JsonPath,
    string Code,
    string Message,
    string RepairSuggestion,
    IReadOnlyList<string> AllowedKinds,
    IReadOnlyList<string> AllowedValues,
    string? ParentSlot);

internal sealed record BlueprintValidationContext(
    IReadOnlySet<string> DeclaredPackIds,
    IReadOnlyDictionary<string, UiBlockDefinition> Blocks,
    IReadOnlyDictionary<string, string[]> PackKinds);
