using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed partial class BlueprintCompositionService(PackRegistry registry)
{
    private const int MaxSummaryProperties = 32;
    private const int MaxSummaryValueCharacters = 160;

    public BlueprintCompositionResult Compose(
        string blueprintJson,
        string targetPath,
        string kind,
        JsonElement? properties = null,
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

        if (!TryCreateConfiguredNode(skeleton, properties, out var configuredNode, out var propertiesIssue))
        {
            return new BlueprintCompositionResult(false, null, null, null, null, [propertiesIssue!]);
        }

        if (!TryResolveTarget(
                blueprint,
                targetPath,
                out var target,
                out var resolvedTargetPath,
                out var issue))
        {
            return new BlueprintCompositionResult(false, null, null, null, null, [issue!]);
        }

        var targetSlot = target!;
        var index = insertionIndex ?? targetSlot.Count;
        if (index < 0 || index > targetSlot.Count)
        {
            return Failure(resolvedTargetPath, "CompositionIndexOutOfRange",
                $"Insertion index {index} is outside the target slot range 0..{targetSlot.Count}.",
                "Omit insertionIndex to append, or choose an index within the target slot range.");
        }

        targetSlot.Insert(index, configuredNode);
        var insertedPath = $"{resolvedTargetPath}[{index}]";
        var candidateJson = blueprint.ToJsonString();
        if (candidateJson.Length > BoundaryStringLimits.MaxStringifiedJsonArgumentLength)
        {
            return Failure(resolvedTargetPath, "BlueprintCompositionTooLarge",
                $"Composed blueprint has {candidateJson.Length} characters; the reusable input maximum is {BoundaryStringLimits.MaxStringifiedJsonArgumentLength}.",
                "Reduce the current blueprint or inserted block properties before composing again.");
        }

        var validation = new BlueprintValidationService(registry).Validate(candidateJson);
        if (!validation.Success)
        {
            return new BlueprintCompositionResult(false, null, null, null, validation, [])
            {
                InvalidCandidate = blueprint,
                CandidateBlueprintJson = candidateJson
            };
        }

        return new BlueprintCompositionResult(
            true,
            blueprint,
            candidateJson,
            insertedPath,
            validation,
            [])
        {
            InsertedNodeSummary = CreateInsertedNodeSummary(configuredNode!, insertedPath)
        };
    }

    private static BlueprintCompositionNodeSummary CreateInsertedNodeSummary(
        JsonNode configuredNode,
        string insertedPath)
    {
        var configuredObject = configuredNode.AsObject();
        var properties = configuredObject["properties"] as JsonObject;
        var propertyCount = properties?.Count ?? 0;
        var propertySummaries = properties?
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Take(MaxSummaryProperties)
            .Select(property => SummarizeProperty(property.Key, property.Value))
            .ToArray() ?? [];
        return new BlueprintCompositionNodeSummary(
            insertedPath,
            configuredObject["kind"]?.GetValue<string>() ?? string.Empty,
            configuredObject["elementName"] is JsonValue elementName
                && elementName.TryGetValue<string>(out var name)
                    ? name
                    : null,
            propertyCount,
            propertySummaries.Length,
            propertyCount > propertySummaries.Length,
            propertySummaries);
    }

    private static BlueprintCompositionPropertySummary SummarizeProperty(
        string name,
        JsonNode? value)
    {
        var raw = value?.ToJsonString() ?? "null";
        using var document = JsonDocument.Parse(raw);
        return new BlueprintCompositionPropertySummary(
            name,
            document.RootElement.ValueKind.ToString(),
            raw.Length <= MaxSummaryValueCharacters
                ? raw
                : raw[..(MaxSummaryValueCharacters - 3)] + "...",
            raw.Length > MaxSummaryValueCharacters);
    }

    private static bool TryCreateConfiguredNode(
        JsonElement skeleton,
        JsonElement? properties,
        out JsonNode? configuredNode,
        out BlueprintCompositionIssue? issue)
    {
        configuredNode = JsonNode.Parse(skeleton.GetRawText());
        issue = null;
        if (properties is null || properties.Value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (properties.Value.ValueKind != JsonValueKind.Object || configuredNode is not JsonObject configuredObject)
        {
            issue = new BlueprintCompositionIssue(
                "$.properties",
                "InvalidCompositionProperties",
                "Composition properties must be a JSON object.",
                "Pass an object containing only property values declared by the selected block kind.");
            return false;
        }

        var suppliedProperties = properties.Value.EnumerateObject().ToArray();
        if (suppliedProperties.Length == 0)
        {
            return true;
        }

        var targetProperties = configuredObject["properties"] as JsonObject ?? new JsonObject();
        configuredObject["properties"] = targetProperties;
        foreach (var property in suppliedProperties)
        {
            targetProperties[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        return true;
    }

    private static bool TryResolveTarget(
        JsonObject blueprint,
        string targetPath,
        out JsonArray? target,
        out string resolvedTargetPath,
        out BlueprintCompositionIssue? issue)
    {
        target = null;
        resolvedTargetPath = targetPath;
        issue = null;
        var resolution = BlueprintNodePathResolver.Resolve(blueprint, targetPath);
        if (!resolution.Success)
        {
            issue = new BlueprintCompositionIssue(
                targetPath,
                resolution.Code switch
                {
                    "ElementAliasNotFound" => "CompositionTargetElementNotFound",
                    "ElementAliasAmbiguous" => "CompositionTargetElementAmbiguous",
                    _ => "InvalidCompositionTargetPath"
                },
                resolution.Message!,
                resolution.RepairSuggestion!);
            return false;
        }

        resolvedTargetPath = resolution.JsonPath;
        var match = TargetPathPattern().Match(resolvedTargetPath);
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
                    resolvedTargetPath,
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
                    resolvedTargetPath,
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
    IReadOnlyList<BlueprintCompositionIssue> Errors)
{
    public JsonObject? InvalidCandidate { get; init; }
    public string? CandidateBlueprintJson { get; init; }
    public BlueprintCompositionNodeSummary? InsertedNodeSummary { get; init; }
}

internal sealed record BlueprintCompositionNodeSummary(
    string JsonPath,
    string Kind,
    string? ElementName,
    int PropertyCount,
    int ReportedPropertyCount,
    bool PropertiesTruncated,
    IReadOnlyList<BlueprintCompositionPropertySummary> Properties);

internal sealed record BlueprintCompositionPropertySummary(
    string Name,
    string ValueKind,
    string CompactValue,
    bool ValueTruncated);

internal sealed record BlueprintCompositionIssue(
    string JsonPath,
    string Code,
    string Message,
    string RepairSuggestion);
