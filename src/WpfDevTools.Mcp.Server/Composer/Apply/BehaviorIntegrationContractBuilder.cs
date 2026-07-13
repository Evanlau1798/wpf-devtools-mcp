using System.Text.Json;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static partial class BehaviorIntegrationContractBuilder
{
    public static BehaviorIntegrationContractPlan Build(PackRegistry registry, string blueprintJson)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
            blueprintJson,
            "<inline-blueprint>",
            UiComposerSchemaVersions.UiBlueprint);
        var interactions = new List<BehaviorInteractionPlan>();
        Collect(blueprint.Layout, LoadInteractionContracts(registry, blueprint), interactions);

        return new BehaviorIntegrationContractPlan(
            interactions.Count == 0 ? "not-detected" : "required",
            GetMappedString(blueprint.Metadata, "recipeId"),
            interactions,
            interactions.Count == 0
                ? "No command-bound interactions were detected in this blueprint."
                : "Implement every commandPath on the generated view DataContext before treating the UI as functional.",
            interactions.Count == 0
                ? "Validate the final application according to its intended interaction contract."
                : "Build and launch the final app, invoke every listed interaction, and verify a state or visible content change for each one.");
    }

    private static IReadOnlyDictionary<string, UiBlockInteraction> LoadInteractionContracts(
        PackRegistry registry,
        UiBlueprint blueprint)
    {
        var declaredIds = blueprint.Packs.Select(pack => pack.Id).ToHashSet(StringComparer.Ordinal);
        return registry.ListPacks().Packs
            .Where(pack => declaredIds.Contains(pack.Id))
            .SelectMany(pack => ComposerPackLoader.Load(pack.RootPath).Blocks)
            .Where(block => block.Interaction is not null)
            .ToDictionary(block => block.Kind, block => block.Interaction!, StringComparer.Ordinal);
    }

    private static void Collect(
        UiBlueprintNode node,
        IReadOnlyDictionary<string, UiBlockInteraction> contracts,
        List<BehaviorInteractionPlan> interactions)
    {
        if (contracts.TryGetValue(node.Kind, out var contract))
        {
            var commandPath = ExtractBindingPath(GetMappedString(node.Properties, contract.CommandProperty));
            if (!string.IsNullOrWhiteSpace(commandPath))
            {
                interactions.Add(new BehaviorInteractionPlan(
                    contract.Kind,
                    commandPath,
                    GetMappedString(node.Properties, contract.CommandParameterProperty),
                    GetMappedString(node.Properties, contract.TargetProperty),
                    GetMappedString(node.Properties, contract.LabelProperty),
                    contract.Kind == "navigation"
                        ? "Map the command parameter to application navigation, selected state, and destination content."
                        : "Implement the command with observable application behavior and an appropriate CanExecute policy."));
            }
        }

        foreach (var children in node.Slots.Values)
        {
            foreach (var child in children)
            {
                Collect(child, contracts, interactions);
            }
        }
    }

    private static string? GetMappedString(IReadOnlyDictionary<string, JsonElement> values, string propertyName)
        => !string.IsNullOrWhiteSpace(propertyName)
            && values.TryGetValue(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ExtractBindingPath(string? binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            return null;
        }

        var match = SimpleBindingPattern().Match(binding);
        return match.Success ? match.Groups["path"].Value : null;
    }

    [GeneratedRegex(@"^\{Binding\s+(?:Path\s*=\s*)?(?<path>[A-Za-z_][A-Za-z0-9_.]*)(?:\s*,\s*[A-Za-z_][A-Za-z0-9_]*\s*=\s*[^,{}]+)*\s*\}$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleBindingPattern();
}
