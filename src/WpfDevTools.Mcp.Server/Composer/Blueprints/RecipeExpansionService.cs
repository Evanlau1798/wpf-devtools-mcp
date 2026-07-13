using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed class RecipeExpansionService(PackRegistry registry)
{
    private static readonly Regex TokenPattern = new(
        @"\{\{\s*(?:inputs?\.)?(?<name>[A-Za-z0-9_.-]+)\s*\}\}",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public RecipeExpansionResult Expand(RecipeExpansionRequest request)
    {
        var errors = new List<BlueprintValidationIssue>();
        var warnings = new List<BlueprintValidationIssue>();
        var registryResult = registry.ListPacks();
        var match = FindRecipe(request.RecipeId, registryResult.Packs);

        if (match is null)
        {
            errors.Add(Issue(
                "$.recipeId",
                "RecipeNotFound",
                $"Recipe '{request.RecipeId}' was not found.",
                "Call get_ui_block_catalog with includeRecipes=true and choose a listed recipe id."));
            return RecipeExpansionResult.Failed(request.RecipeId, errors, warnings, registryResult.Diagnostics);
        }

        ValidateRecipeContract(match.Value.Pack, match.Value.Recipe, errors);
        var inputs = ResolveInputs(match.Value.Recipe, request.Inputs, errors, warnings);
        if (errors.Count > 0)
        {
            return RecipeExpansionResult.Failed(request.RecipeId, errors, warnings, registryResult.Diagnostics);
        }

        var expandedNode = Substitute(match.Value.Recipe.ExpandsTo, inputs);
        var layout = JsonSerializer.Deserialize<UiBlueprintNode>(expandedNode.ToJsonString(), JsonOptions) ?? new UiBlueprintNode();
        var blueprint = new UiBlueprint
        {
            SchemaVersion = UiComposerSchemaVersions.UiBlueprint,
            Name = match.Value.Recipe.DisplayName,
            Packs = match.Value.Recipe.RequiredPacks,
            PrimaryPack = match.Value.Recipe.PackId,
            Layout = layout,
            Metadata =
            {
                ["recipeId"] = JsonSerializer.SerializeToElement(match.Value.Recipe.Id),
                ["recipePackId"] = JsonSerializer.SerializeToElement(match.Value.Pack.Id)
            }
        };

        var validation = new BlueprintValidationService(registry).Validate(JsonSerializer.Serialize(blueprint, JsonOptions));
        return new RecipeExpansionResult(
            validation.Success && errors.Count == 0,
            match.Value.Recipe.Id,
            blueprint,
            validation,
            errors,
            warnings,
            registryResult.Diagnostics);
    }

    private static (PackRegistryItem Pack, UiRecipeDefinition Recipe)? FindRecipe(
        string recipeId,
        IReadOnlyList<PackRegistryItem> packs)
    {
        foreach (var pack in packs)
        {
            var loadedPack = ComposerPackLoader.Load(pack.RootPath);
            var recipe = loadedPack.Recipes.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, recipeId, StringComparison.Ordinal));
            if (recipe is not null)
            {
                return (pack, recipe);
            }
        }

        return null;
    }

    private static void ValidateRecipeContract(
        PackRegistryItem pack,
        UiRecipeDefinition recipe,
        List<BlueprintValidationIssue> errors)
    {
        if (!string.Equals(recipe.PackId, pack.Id, StringComparison.Ordinal))
        {
            errors.Add(Issue("$.packId", "RecipePackIdMismatch", $"Recipe packId '{recipe.PackId}' does not match installed pack '{pack.Id}'.", "Set recipe packId to the owning pack id."));
        }

        if (recipe.RequiredPacks.Length == 0)
        {
            errors.Add(Issue("$.requiredPacks", "RequiredPacksMissing", "Recipe requiredPacks[] must declare the packs needed for expansion.", "Add the primary pack reference with id and version."));
        }

        if (recipe.ExpandsTo.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Issue("$.expandsTo", "RecipeExpansionMissing", "Recipe expandsTo must be a blueprint fragment object.", "Set expandsTo to a block object with a pack-qualified kind."));
        }
    }

    private static Dictionary<string, JsonElement> ResolveInputs(
        UiRecipeDefinition recipe,
        JsonElement? providedInputs,
        List<BlueprintValidationIssue> errors,
        List<BlueprintValidationIssue> warnings)
    {
        var resolved = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, input) in recipe.Inputs)
        {
            if (input.Default is JsonElement defaultValue)
            {
                resolved[name] = defaultValue.Clone();
            }
        }

        if (providedInputs is JsonElement provided)
        {
            if (provided.ValueKind != JsonValueKind.Object)
            {
                errors.Add(Issue("$.inputs", "RecipeInputsMustBeObject", "Recipe inputs must be a JSON object.", "Pass inputs as an object keyed by recipe input name."));
            }
            else
            {
                foreach (var property in provided.EnumerateObject())
                {
                    if (!recipe.Inputs.ContainsKey(property.Name))
                    {
                        warnings.Add(Issue($"$.inputs.{property.Name}", "UnknownRecipeInput", $"Recipe does not define input '{property.Name}'.", $"Remove input '{property.Name}' or choose a recipe that declares it."));
                        continue;
                    }

                    resolved[property.Name] = property.Value.Clone();
                }
            }
        }

        foreach (var (name, input) in recipe.Inputs)
        {
            if (!resolved.TryGetValue(name, out var value))
            {
                if (input.Required)
                {
                    errors.Add(Issue($"$.inputs.{name}", "RequiredRecipeInputMissing", $"Recipe input '{name}' is required.", $"Provide input '{name}' with type '{input.Type}'."));
                }

                continue;
            }

            if (!MatchesInputType(value, input.Type))
            {
                errors.Add(Issue($"$.inputs.{name}", "RecipeInputTypeMismatch", $"Recipe input '{name}' must be type '{input.Type}'.", $"Set input '{name}' to a JSON value compatible with '{input.Type}'."));
                continue;
            }

            var allowedValues = GetAllowedValues(input);
            if (allowedValues.Length > 0
                && value.ValueKind == JsonValueKind.String
                && !allowedValues.Contains(value.GetString() ?? string.Empty, StringComparer.Ordinal))
            {
                errors.Add(Issue($"$.inputs.{name}", "RecipeInputValueNotAllowed", $"Recipe input '{name}' value '{value.GetString()}' is not allowed.", $"Use one of: {string.Join(", ", allowedValues)}.", allowedValues: allowedValues));
            }
        }

        return resolved;
    }

    private static JsonNode Substitute(JsonElement template, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var node = JsonNode.Parse(template.GetRawText())
            ?? throw new InvalidDataException("Recipe expandsTo could not be parsed.");
        return SubstituteNode(node, inputs) ?? new JsonObject();
    }

    private static JsonNode? SubstituteNode(JsonNode? node, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var (key, child) in obj)
            {
                result[key] = SubstituteNode(child, inputs);
            }

            return result;
        }

        if (node is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var child in array)
            {
                result.Add(SubstituteNode(child, inputs));
            }

            return result;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var match = TokenPattern.Match(text);
            if (match.Success && match.Index == 0 && match.Length == text.Length
                && inputs.TryGetValue(match.Groups["name"].Value, out var exactValue))
            {
                return JsonNode.Parse(exactValue.GetRawText());
            }

            var replaced = TokenPattern.Replace(text, match =>
                inputs.TryGetValue(match.Groups["name"].Value, out var input)
                    ? ToReplacementString(input)
                    : match.Value);
            return JsonValue.Create(replaced);
        }

        return node?.DeepClone();
    }

    private static bool MatchesInputType(JsonElement value, string type)
        => type switch
        {
            "binding" or "string" => value.ValueKind == JsonValueKind.String,
            "boolean" or "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => value.ValueKind == JsonValueKind.Number,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => true
        };

    private static string[] GetAllowedValues(UiRecipeInput input)
        => input.AllowedValues.Length > 0 ? input.AllowedValues : input.EnumValues;

    private static string ToReplacementString(JsonElement input)
        => input.ValueKind == JsonValueKind.String
            ? input.GetString() ?? string.Empty
            : input.GetRawText();

    private static BlueprintValidationIssue Issue(
        string jsonPath,
        string code,
        string message,
        string repairSuggestion,
        IReadOnlyList<string>? allowedValues = null)
        => new(jsonPath, code, message, repairSuggestion, [], allowedValues ?? [], null);
}

internal sealed record RecipeExpansionRequest(string RecipeId, JsonElement? Inputs = null);

internal sealed record RecipeExpansionResult(
    bool Success,
    string RecipeId,
    UiBlueprint Blueprint,
    BlueprintValidationResult Validation,
    IReadOnlyList<BlueprintValidationIssue> Errors,
    IReadOnlyList<BlueprintValidationIssue> Warnings,
    IReadOnlyList<string> Diagnostics)
{
    public static RecipeExpansionResult Failed(
        string recipeId,
        IReadOnlyList<BlueprintValidationIssue> errors,
        IReadOnlyList<BlueprintValidationIssue> warnings,
        IReadOnlyList<string> diagnostics)
        => new(Success: false, RecipeId: recipeId, Blueprint: new UiBlueprint(), Validation: new BlueprintValidationResult([], [], [], BlueprintResolutionPlan.Empty), Errors: errors, Warnings: warnings, Diagnostics: diagnostics);
}
