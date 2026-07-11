using System.Globalization;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed class BlueprintValidationService(PackRegistry registry)
{
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

        if (!ValidateNodeCount(blueprint.Layout, errors))
        {
            return new BlueprintValidationResult(errors, warnings, []);
        }

        var registryResult = registry.ListPacks();
        var declaredPacks = ValidatePacks(blueprint, registryResult.Packs, errors, warnings);
        ValidateRequiredFields(blueprint, errors);
        errors.AddRange(ComposerPackRoleValidator.Validate(blueprint));
        ValidatePrimaryPackRole(blueprint, errors);
        warnings.AddRange(BlueprintPackConflictDiagnostics.FindResourceConflicts(declaredPacks));

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
            var declaredPackIds = blueprint.Packs
                .Select(pack => pack.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            var optionalMissingPackIds = blueprint.Packs
                .Where(pack => !pack.Required && !string.IsNullOrWhiteSpace(pack.Id) && !declaredPacks.ContainsKey(pack.Id))
                .Select(pack => pack.Id)
                .ToHashSet(StringComparer.Ordinal);
            var context = BuildContext(declaredPacks, declaredPackIds, optionalMissingPackIds, errors);
            var usedPackIds = new HashSet<string>(StringComparer.Ordinal);
            ValidateNode(blueprint.Layout, "$.layout", null, null, context, usedPackIds, errors, warnings);
            WarnForUnusedDeclaredPacks(blueprint, declaredPacks.Keys.ToHashSet(StringComparer.Ordinal), usedPackIds, warnings);
        }

        return new BlueprintValidationResult(errors, warnings, registryResult.Diagnostics);
    }

    private static bool ValidateNodeCount(UiBlueprintNode root, List<BlueprintValidationIssue> errors)
    {
        var count = 0;
        var stack = new Stack<UiBlueprintNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            count++;
            if (count > ComposerPerformanceTargets.MaxBlueprintNodeCount)
            {
                errors.Add(Issue(
                    "$.layout",
                    "BlueprintTooLarge",
                    $"Blueprint contains more than {ComposerPerformanceTargets.MaxBlueprintNodeCount} nodes.",
                    "Split the blueprint into smaller compositions or reduce generated nodes."));
                return false;
            }

            foreach (var children in node.Slots.Values)
            {
                foreach (var child in children)
                {
                    stack.Push(child);
                }
            }
        }

        return true;
    }

    private static Dictionary<string, PackRegistryItem> ValidatePacks(
        UiBlueprint blueprint,
        IReadOnlyList<PackRegistryItem> availablePacks,
        List<BlueprintValidationIssue> errors,
        List<BlueprintValidationIssue> warnings)
    {
        var availableById = availablePacks.ToDictionary(pack => pack.Id, StringComparer.Ordinal);
        var declared = new Dictionary<string, PackRegistryItem>(StringComparer.Ordinal);
        var requestedVersions = new Dictionary<string, string>(StringComparer.Ordinal);

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

            if (requestedVersions.TryGetValue(packRef.Id, out var requestedVersion)
                && !string.Equals(requestedVersion, packRef.Version, StringComparison.Ordinal))
            {
                errors.Add(Issue(path + ".version", "PackVersionConflict", $"Pack '{packRef.Id}' declares conflicting versions {requestedVersion} and {packRef.Version}.", "Keep one version per pack id in packs[]."));
                continue;
            }

            requestedVersions[packRef.Id] = packRef.Version;

            if (!availableById.TryGetValue(packRef.Id, out var available))
            {
                var issue = Issue(
                    path,
                    packRef.Required ? "PackNotFound" : "OptionalPackMissing",
                    packRef.Required
                        ? $"Pack '{packRef.Id}' {packRef.Version} is not installed."
                        : $"Optional pack '{packRef.Id}' {packRef.Version} is not installed.",
                    packRef.Required
                        ? "Install or reference a Composer pack before validating this blueprint."
                        : "Install the optional pack to use its blocks, or remove it from packs[] if it is not needed.");
                if (packRef.Required)
                {
                    errors.Add(issue);
                }
                else
                {
                    warnings.Add(issue);
                }

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

    private static void ValidatePrimaryPackRole(UiBlueprint blueprint, List<BlueprintValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(blueprint.PrimaryPack))
        {
            return;
        }

        for (var index = 0; index < blueprint.Packs.Length; index++)
        {
            var pack = blueprint.Packs[index];
            if (!string.Equals(pack.Id, blueprint.PrimaryPack, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(pack.Role, ComposerPackRoles.Primary, StringComparison.Ordinal))
            {
                errors.Add(Issue(
                    $"$.packs[{index}].role",
                    "PrimaryPackRoleMismatch",
                    $"Primary pack '{blueprint.PrimaryPack}' must be declared with role 'primary'.",
                    "Set the matching packs[] entry role to 'primary'."));
            }

            return;
        }
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
        IReadOnlySet<string> declaredPackIds,
        IReadOnlySet<string> optionalMissingPackIds,
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

        return new BlueprintValidationContext(declaredPackIds, declaredPacks.Keys.ToHashSet(StringComparer.Ordinal), optionalMissingPackIds, blocks, packKinds);
    }

    private static void ValidateNode(
        UiBlueprintNode node,
        string path,
        string? parentSlot,
        IReadOnlyList<string>? allowedKinds,
        BlueprintValidationContext context,
        HashSet<string> usedPackIds,
        List<BlueprintValidationIssue> errors,
        List<BlueprintValidationIssue> warnings)
    {
        if (string.IsNullOrWhiteSpace(node.Kind))
        {
            errors.Add(Issue(path + ".kind", "RequiredFieldMissing", "Block kind is required.", "Set kind to a pack-qualified block kind."));
            return;
        }

        if (!node.Kind.Contains('.', StringComparison.Ordinal))
        {
            errors.Add(Issue(path, "UnqualifiedBlockKind", $"Block kind '{node.Kind}' is not pack-qualified.", "Use a pack-qualified kind such as wpfui.button."));
            return;
        }

        var packId = ComposerPackKindResolver.ResolveDeclaredPackId(node.Kind, context.DeclaredPackIds);
        if (packId is null)
        {
            packId = ComposerPackKindResolver.GetFallbackPackId(node.Kind);
            errors.Add(Issue(path, "PackNotDeclared", $"Block kind '{node.Kind}' uses undeclared pack '{packId}'.", $"Add pack '{packId}' with an explicit version to packs[] or choose a declared pack block."));
            return;
        }

        usedPackIds.Add(packId);
        if (!context.LoadedPackIds.Contains(packId))
        {
            var code = context.OptionalMissingPackIds.Contains(packId) ? "OptionalPackMissing" : "PackNotFound";
            errors.Add(Issue(path, code, $"Block kind '{node.Kind}' uses pack '{packId}', but that pack is not installed.", $"Install pack '{packId}' or remove block kind '{node.Kind}'."));
            return;
        }

        if (!context.Blocks.TryGetValue(node.Kind, out var block))
        {
            errors.Add(Issue(path, "UnknownBlockKind", $"Block kind '{node.Kind}' was not found in pack '{packId}'.", BuildUnknownKindSuggestion(context, packId)));
            return;
        }

        if (allowedKinds is not null && !IsAllowedKind(node.Kind, allowedKinds))
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
        ValidateSlots(node, path, block, context, usedPackIds, errors, warnings);
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
                errors.Add(Issue($"{path}.properties.{name}", "UnknownProperty", $"Block '{block.Kind}' does not define property '{name}'.", $"Remove property '{name}' or update the pack contract."));
                continue;
            }

            if (!MatchesPropertyType(value, property.Type))
            {
                errors.Add(Issue($"{path}.properties.{name}", "PropertyTypeMismatch", $"Property '{name}' must be type '{property.Type}'.", $"Set property '{name}' to a JSON value compatible with '{property.Type}'."));
                continue;
            }

            ValidatePropertyConstraints(path, name, value, property, errors);

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
        HashSet<string> usedPackIds,
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
                ValidateNode(children[index], $"{path}.slots.{slotName}[{index}]", slotName, slot.AllowedKinds, context, usedPackIds, errors, warnings);
            }
        }
    }

    private static void WarnForUnusedDeclaredPacks(
        UiBlueprint blueprint,
        IReadOnlySet<string> declaredPackIds,
        IReadOnlySet<string> usedPackIds,
        List<BlueprintValidationIssue> warnings)
    {
        for (var index = 0; index < blueprint.Packs.Length; index++)
        {
            var pack = blueprint.Packs[index];
            if (!declaredPackIds.Contains(pack.Id) || usedPackIds.Contains(pack.Id))
            {
                continue;
            }

            warnings.Add(Issue(
                $"$.packs[{index}]",
                "UnusedPack",
                $"Pack '{pack.Id}' is declared but no block kind from that pack is used.",
                $"Remove pack '{pack.Id}' from packs[] or use a pack-qualified block kind from that pack."));
        }
    }

    private static bool MatchesPropertyType(JsonElement value, string type)
        => type switch
        {
            "binding" or "string" => value.ValueKind == JsonValueKind.String,
            "boolean" or "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => value.ValueKind == JsonValueKind.Number,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => false
        };

    private static bool IsAllowedKind(string kind, IReadOnlyList<string> patterns)
        => patterns.Any(pattern => pattern == "*"
            || string.Equals(pattern, kind, StringComparison.Ordinal)
            || pattern.EndsWith(".*", StringComparison.Ordinal)
            && kind.StartsWith(pattern[..^1], StringComparison.Ordinal));

    private static void ValidatePropertyConstraints(
        string path,
        string name,
        JsonElement value,
        UiBlockProperty property,
        List<BlueprintValidationIssue> errors)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            if (property.Minimum is double minimum && number < minimum)
            {
                errors.Add(Issue($"{path}.properties.{name}", "PropertyMinimumViolation", $"Property '{name}' must be at least {minimum}.", $"Set property '{name}' to {minimum} or greater."));
            }

            if (property.Maximum is double maximum && number > maximum)
            {
                errors.Add(Issue($"{path}.properties.{name}", "PropertyMaximumViolation", $"Property '{name}' must be at most {maximum}.", $"Set property '{name}' to {maximum} or less."));
            }

            if (property.Integer && number != Math.Truncate(number))
            {
                errors.Add(Issue($"{path}.properties.{name}", "PropertyIntegerRequired", $"Property '{name}' must be an integer.", $"Set property '{name}' to a whole number."));
            }
        }

        if (!string.IsNullOrWhiteSpace(property.Format)
            && value.ValueKind == JsonValueKind.String
            && !MatchesFormat(value.GetString() ?? string.Empty, property.Format))
        {
            errors.Add(Issue($"{path}.properties.{name}", "PropertyFormatMismatch", $"Property '{name}' must use format '{property.Format}'.", $"Set property '{name}' to a valid {property.Format} value."));
        }
    }

    private static bool MatchesFormat(string value, string format)
        => format switch
        {
            "thickness" => IsThickness(value),
            "gridLength" => IsGridLength(value),
            _ => false
        };

    private static bool IsThickness(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length is 1 or 2 or 4
            && parts.All(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
    }

    private static bool IsGridLength(string value)
    {
        if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase) || value == "*")
        {
            return true;
        }

        var number = value.EndsWith('*') ? value[..^1] : value;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0;
    }

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
