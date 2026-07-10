using System.Text.Json;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static partial class BehaviorIntegrationContractBuilder
{
    public static BehaviorIntegrationContractPlan Build(string blueprintJson)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
            blueprintJson,
            "<inline-blueprint>",
            UiComposerSchemaVersions.UiBlueprint);
        var interactions = new List<BehaviorInteractionPlan>();
        Collect(blueprint.Layout, interactions);

        return new BehaviorIntegrationContractPlan(
            interactions.Count == 0 ? "not-detected" : "required",
            GetString(blueprint.Metadata, "recipeId"),
            interactions,
            interactions.Count == 0
                ? "No command-bound interactions were detected in this blueprint."
                : "Implement every commandPath on the generated view DataContext before treating the UI as functional.",
            interactions.Count == 0
                ? "Validate the final application according to its intended interaction contract."
                : "Build and launch the final app, invoke every listed interaction, and verify a state or visible content change for each one.");
    }

    private static void Collect(UiBlueprintNode node, List<BehaviorInteractionPlan> interactions)
    {
        var command = GetString(node.Properties, "command");
        var commandPath = ExtractBindingPath(command);
        if (!string.IsNullOrWhiteSpace(commandPath))
        {
            var isNavigation = node.Kind.EndsWith("navigationViewItem", StringComparison.Ordinal);
            interactions.Add(new BehaviorInteractionPlan(
                isNavigation ? "navigation" : "action",
                commandPath,
                GetString(node.Properties, "commandParameter"),
                GetString(node.Properties, "targetPageTag"),
                GetLabel(node),
                isNavigation
                    ? "Map the command parameter to application navigation, selected state, and destination content."
                    : "Implement the command with observable application behavior and an appropriate CanExecute policy."));
        }

        foreach (var children in node.Slots.Values)
        {
            foreach (var child in children)
            {
                Collect(child, interactions);
            }
        }
    }

    private static string? GetLabel(UiBlueprintNode node)
    {
        var text = GetString(node.Properties, "text") ?? GetString(node.Properties, "value");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        foreach (var children in node.Slots.Values)
        {
            foreach (var child in children)
            {
                var label = GetLabel(child);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    return label;
                }
            }
        }

        return null;
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> values, string name)
        => values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
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
