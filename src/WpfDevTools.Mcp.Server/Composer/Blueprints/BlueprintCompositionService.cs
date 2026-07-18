using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed class BlueprintCompositionService(PackRegistry registry)
{
    private const int MaxSummaryProperties = 32;
    private const int MaxSummaryValueCharacters = 160;

    public BlueprintCompositionResult Compose(
        string blueprintJson,
        string targetPath,
        string kind,
        string? elementName = null,
        string? automationId = null,
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

        if (!TryCreateConfiguredNode(
                skeleton,
                elementName,
                automationId,
                properties,
                out var configuredNode,
                out var propertiesIssue))
        {
            return new BlueprintCompositionResult(false, null, null, null, null, [propertiesIssue!]);
        }

        if (!TryResolveTarget(
                blueprint,
                targetPath,
                out var target,
                out var targetParent,
                out var targetSlotName,
                out var resolvedTargetPath,
                out var issue))
        {
            return new BlueprintCompositionResult(false, null, null, null, null, [issue!]);
        }

        var targetSlot = target!;
        var existingCount = targetSlot.Count;
        var index = insertionIndex ?? targetSlot.Count;
        if (index < 0 || index > targetSlot.Count)
        {
            return Failure(resolvedTargetPath, "CompositionIndexOutOfRange",
                $"Insertion index {index} is outside the target slot range 0..{targetSlot.Count}.",
                "Omit insertionIndex to append, or choose an index within the target slot range.");
        }

        targetSlot.Insert(index, configuredNode);
        var targetSlotSummary = CreateTargetSlotSummary(
            targetParent!,
            targetSlotName,
            resolvedTargetPath,
            existingCount,
            targetSlot.Count);
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
                CandidateBlueprintJson = candidateJson,
                TargetSlotSummary = targetSlotSummary
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
            InsertedNodeSummary = CreateInsertedNodeSummary(configuredNode!, insertedPath),
            TargetSlotSummary = targetSlotSummary
        };
    }

    private BlueprintCompositionSlotSummary? CreateTargetSlotSummary(
        JsonObject targetParent,
        string slotName,
        string targetPath,
        int existingCount,
        int resultingCount)
    {
        var parentKind = targetParent["kind"]?.GetValue<string>();
        if (parentKind is null)
        {
            return null;
        }

        var parent = new BlockCatalogService(registry)
            .GetCatalog(new BlockCatalogQuery(Kind: parentKind))
            .Items.SingleOrDefault();
        if (parent is null || !parent.Slots.TryGetValue(slotName, out var slot))
        {
            return null;
        }

        return new BlueprintCompositionSlotSummary(
            targetPath,
            parentKind,
            slotName,
            existingCount,
            resultingCount,
            slot.MinItems,
            slot.MaxItems,
            slot.MaxItems is int maximum ? Math.Max(0, maximum - resultingCount) : null,
            slot.MaxItems is int limit && resultingCount > limit,
            slot.AllowedKinds);
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
            configuredObject["automationId"] is JsonValue automationId
                && automationId.TryGetValue<string>(out var id)
                    ? id
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
        string? elementName,
        string? automationId,
        JsonElement? properties,
        out JsonNode? configuredNode,
        out BlueprintCompositionIssue? issue)
    {
        configuredNode = JsonNode.Parse(skeleton.GetRawText());
        issue = null;
        if (configuredNode is JsonObject identifiedObject)
        {
            if (elementName is not null)
            {
                identifiedObject["elementName"] = elementName;
            }

            if (automationId is not null)
            {
                identifiedObject["automationId"] = automationId;
            }
        }

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

    private bool TryResolveTarget(
        JsonObject blueprint,
        string targetPath,
        out JsonArray? target,
        out JsonObject? targetParent,
        out string targetSlotName,
        out string resolvedTargetPath,
        out BlueprintCompositionIssue? issue)
    {
        target = null;
        targetParent = null;
        targetSlotName = string.Empty;
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
        if (!BlueprintCompositionTargetPath.TryParse(resolvedTargetPath, out var segments)
            || segments[^1].ChildIndex is not null
            || segments.Take(segments.Count - 1).Any(segment => segment.ChildIndex is null))
        {
            issue = InvalidPath(targetPath);
            return false;
        }

        JsonObject? current = blueprint["layout"] as JsonObject;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            JsonObject? slots = null;
            JsonArray? children = null;
            if (current is not null && current.TryGetPropertyValue("slots", out var slotsNode))
            {
                if (slotsNode is not JsonObject existingSlots)
                {
                    issue = InvalidTargetShape(resolvedTargetPath, "The parent slots value must be a JSON object.");
                    return false;
                }

                slots = existingSlots;
                if (slots.TryGetPropertyValue(segment.SlotName, out var slotNode))
                {
                    if (slotNode is not JsonArray existingChildren)
                    {
                        issue = InvalidTargetShape(
                            resolvedTargetPath,
                            $"Target slot '{segment.SlotName}' must be a JSON array.");
                        return false;
                    }

                    children = existingChildren;
                }
            }

            if (children is null
                && i == segments.Count - 1
                && current is not null
                && IsDeclaredSlot(current, segment.SlotName))
            {
                slots ??= new JsonObject();
                current["slots"] = slots;
                children = new JsonArray();
                slots[segment.SlotName] = children;
            }

            if (children is null)
            {
                issue = new BlueprintCompositionIssue(
                    resolvedTargetPath,
                    "CompositionTargetNotFound",
                    $"Target slot '{segment.SlotName}' does not exist at the requested blueprint path.",
                    "Use a targetPath from validate_ui_blueprint compositionMap.targets.");
                return false;
            }

            if (i == segments.Count - 1)
            {
                target = children;
                targetParent = current;
                targetSlotName = segment.SlotName;
                return true;
            }

            var index = segment.ChildIndex!.Value;

            if (index < 0 || index >= children.Count || children[index] is not JsonObject child)
            {
                issue = new BlueprintCompositionIssue(
                    resolvedTargetPath,
                    "CompositionTargetNotFound",
                    $"Child index {index} does not identify a block at slot '{segment.SlotName}'.",
                    "Choose an existing child index before navigating to its nested slot.");
                return false;
            }

            current = child;
        }

        issue = InvalidPath(targetPath);
        return false;
    }

    private bool IsDeclaredSlot(JsonObject node, string slotName)
    {
        var kind = node["kind"]?.GetValue<string>();
        return kind is not null
               && new BlockCatalogService(registry)
                   .GetCatalog(new BlockCatalogQuery(Kind: kind))
                   .Items.SingleOrDefault() is { } block
               && block.Slots.ContainsKey(slotName);
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
            "Use a targetPath from validate_ui_blueprint compositionMap.targets.");

    private static BlueprintCompositionIssue InvalidTargetShape(string targetPath, string message)
        => new(
            targetPath,
            "CompositionTargetInvalidShape",
            message,
            "Repair the existing blueprint slots shape before composing; object slots contain array-valued children.");
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
    public BlueprintCompositionSlotSummary? TargetSlotSummary { get; init; }
}

internal sealed record BlueprintCompositionNodeSummary(
    string JsonPath,
    string Kind,
    string? ElementName,
    string? AutomationId,
    int PropertyCount,
    int ReportedPropertyCount,
    bool PropertiesTruncated,
    IReadOnlyList<BlueprintCompositionPropertySummary> Properties);

internal sealed record BlueprintCompositionPropertySummary(
    string Name,
    string ValueKind,
    string CompactValue,
    bool ValueTruncated);

internal sealed record BlueprintCompositionSlotSummary(
    string TargetPath,
    string ParentKind,
    string SlotName,
    int ExistingCount,
    int ResultingCount,
    int MinItems,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    int? MaxItems,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    int? RemainingCapacity,
    bool CapacityExceeded,
    IReadOnlyList<string> AllowedKinds);

internal sealed record BlueprintCompositionIssue(
    string JsonPath,
    string Code,
    string Message,
    string RepairSuggestion);
