using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed partial class BlueprintCompositionService(PackRegistry registry)
{
    public BlueprintCompositionResult Compose(
        string blueprintJson,
        string targetPath,
        string kind,
        int? insertionIndex = null)
    {
        JsonObject blueprint;
        try
        {
            blueprint = JsonNode.Parse(blueprintJson) as JsonObject
                ?? throw new JsonException("Blueprint root must be a JSON object.");
        }
        catch (JsonException ex)
        {
            return Failure("$", "InvalidBlueprintJson", ex.Message,
                "Provide a valid blueprint JSON object.");
        }

        var item = new BlockCatalogService(registry)
            .GetCatalog(new BlockCatalogQuery(ComposableOnly: true, Kind: kind))
            .Items.SingleOrDefault();
        if (item?.CompositionSkeleton is not JsonElement skeleton)
        {
            return Failure("$.kind", "BlockNotComposable",
                $"Block kind '{kind}' has no available composition skeleton.",
                "Choose a renderer-backed kind from get_ui_block_catalog(composableOnly=true).");
        }

        if (!TryResolveTarget(blueprint, targetPath, out var target, out var issue))
        {
            return new BlueprintCompositionResult(false, null, null, null, null, [issue!]);
        }

        var targetSlot = target!;
        var index = insertionIndex ?? targetSlot.Count;
        if (index < 0 || index > targetSlot.Count)
        {
            return Failure(targetPath, "CompositionIndexOutOfRange",
                $"Insertion index {index} is outside the target slot range 0..{targetSlot.Count}.",
                "Omit insertionIndex to append, or choose an index within the target slot range.");
        }

        targetSlot.Insert(index, JsonNode.Parse(skeleton.GetRawText()));
        var candidateJson = blueprint.ToJsonString();
        var validation = new BlueprintValidationService(registry).Validate(candidateJson);
        if (!validation.Success)
        {
            return new BlueprintCompositionResult(false, null, null, null, validation, []);
        }

        return new BlueprintCompositionResult(
            true,
            blueprint,
            candidateJson,
            $"{targetPath}[{index}]",
            validation,
            []);
    }

    private static bool TryResolveTarget(
        JsonObject blueprint,
        string targetPath,
        out JsonArray? target,
        out BlueprintCompositionIssue? issue)
    {
        target = null;
        issue = null;
        var match = TargetPathPattern().Match(targetPath);
        if (!match.Success)
        {
            issue = InvalidPath(targetPath);
            return false;
        }

        var slotNames = match.Groups["slot"].Captures.Select(capture => capture.Value).ToArray();
        var indices = match.Groups["index"].Captures.Select(capture => capture.Value).ToArray();
        if (indices.Length != slotNames.Length - 1)
        {
            issue = InvalidPath(targetPath);
            return false;
        }

        JsonObject? current = blueprint["layout"] as JsonObject;
        for (var i = 0; i < slotNames.Length; i++)
        {
            var slots = current?["slots"] as JsonObject;
            var children = slots?[slotNames[i]] as JsonArray;
            if (children is null)
            {
                issue = new BlueprintCompositionIssue(
                    targetPath,
                    "CompositionTargetNotFound",
                    $"Target slot '{slotNames[i]}' does not exist at the requested blueprint path.",
                    "Use an existing slots.<name> path from the current blueprint object.");
                return false;
            }

            if (i == slotNames.Length - 1)
            {
                target = children;
                return true;
            }

            if (!int.TryParse(
                    indices[i],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var index))
            {
                issue = InvalidPath(targetPath);
                return false;
            }

            if (index < 0 || index >= children.Count || children[index] is not JsonObject child)
            {
                issue = new BlueprintCompositionIssue(
                    targetPath,
                    "CompositionTargetNotFound",
                    $"Child index {index} does not identify a block at slot '{slotNames[i]}'.",
                    "Choose an existing child index before navigating to its nested slot.");
                return false;
            }

            current = child;
        }

        issue = InvalidPath(targetPath);
        return false;
    }

    private static BlueprintCompositionResult Failure(
        string jsonPath,
        string code,
        string message,
        string repairSuggestion)
        => new(false, null, null, null, null,
            [new BlueprintCompositionIssue(jsonPath, code, message, repairSuggestion)]);

    private static BlueprintCompositionIssue InvalidPath(string targetPath)
        => new(
            targetPath,
            "InvalidCompositionTargetPath",
            "Target path must identify one slot, with an explicit child index before each nested slot.",
            "Use $.layout.slots.<slot> or $.layout.slots.<slot>[0].slots.<nestedSlot>.");

    [GeneratedRegex("^\\$\\.layout(?:\\.slots\\.(?<slot>[A-Za-z_][A-Za-z0-9_-]*)(?:\\[(?<index>[0-9]+)\\])?)+$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetPathPattern();
}

internal sealed record BlueprintCompositionResult(
    bool Composed,
    JsonObject? Blueprint,
    string? BlueprintJson,
    string? InsertedPath,
    BlueprintValidationResult? Validation,
    IReadOnlyList<BlueprintCompositionIssue> Errors);

internal sealed record BlueprintCompositionIssue(
    string JsonPath,
    string Code,
    string Message,
    string RepairSuggestion);
