using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed class BlueprintRepairService(PackRegistry registry)
{
    public BlueprintRepairResult Repair(BlueprintRepairRequest request)
    {
        var actions = new List<BlueprintRepairAction>();
        var validation = new BlueprintValidationService(registry).Validate(
            request.BlueprintJson,
            request.TargetPath);

        actions.AddRange(validation.Errors.Select(issue => ToAction("validation", issue, request.BlueprintJson)));
        actions.AddRange(validation.Warnings.Select(issue => ToAction("validation-warning", issue, request.BlueprintJson)));

        if (validation.Success)
        {
            var render = new UiBlueprintRenderer(registry)
                .Render(new RenderBlueprintRequest(request.BlueprintJson, request.TargetPath));
            actions.AddRange(render.Errors.Select(issue => ToAction(
                "renderer",
                issue,
                request.BlueprintJson,
                FindRendererTemplatePath(render.SourceMap, issue.JsonPath))));
        }

        actions.AddRange(ParseDiagnostics(request.DiagnosticsJson));
        var mergedActions = MergeDuplicateActions(actions);

        return new BlueprintRepairResult(
            Success: true,
            Repairable: mergedActions.Count > 0,
            GeneratedXamlPatch: false,
            ActionCount: mergedActions.Count,
            Actions: mergedActions,
            Diagnostics: validation.Diagnostics);
    }

    private static IReadOnlyList<BlueprintRepairAction> MergeDuplicateActions(
        IReadOnlyList<BlueprintRepairAction> actions)
    {
        var merged = new List<BlueprintRepairAction>(actions.Count);
        foreach (var action in actions)
        {
            var position = merged.FindIndex(candidate => HasEquivalentGuidance(candidate, action));
            if (position < 0)
            {
                merged.Add(action);
                continue;
            }

            var current = merged[position];
            merged[position] = current with
            {
                Sources = current.Sources
                    .Concat(action.Sources)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            };
        }

        return merged;
    }

    private static bool HasEquivalentGuidance(BlueprintRepairAction left, BlueprintRepairAction right)
        => string.Equals(left.Target, right.Target, StringComparison.Ordinal)
           && string.Equals(left.RepairKind, right.RepairKind, StringComparison.Ordinal)
           && string.Equals(left.IssueCode, right.IssueCode, StringComparison.Ordinal)
           && string.Equals(left.JsonPath, right.JsonPath, StringComparison.Ordinal)
           && string.Equals(left.Message, right.Message, StringComparison.Ordinal)
           && string.Equals(left.SuggestedAction, right.SuggestedAction, StringComparison.Ordinal)
           && left.AllowedKinds.SequenceEqual(right.AllowedKinds, StringComparer.Ordinal)
           && left.AllowedValues.SequenceEqual(right.AllowedValues, StringComparer.Ordinal)
           && string.Equals(left.SuggestedValue?.GetRawText(), right.SuggestedValue?.GetRawText(), StringComparison.Ordinal)
           && string.Equals(left.RendererTemplatePath, right.RendererTemplatePath, StringComparison.Ordinal);

    private BlueprintRepairAction ToAction(
        string source,
        BlueprintValidationIssue issue,
        string blueprintJson,
        string? rendererTemplatePath = null)
    {
        var target = GetTarget(issue.Code);
        return new BlueprintRepairAction(
            source,
            target,
            GetRepairKind(issue.Code),
            issue.Code,
            issue.JsonPath,
            issue.Message,
            GetSuggestedAction(issue, target),
            issue.AllowedKinds,
            issue.AllowedValues,
            GetSuggestedValue(issue, blueprintJson),
            rendererTemplatePath);
    }

    private static string? FindRendererTemplatePath(
        IReadOnlyList<RenderSourceMapEntry> sourceMap,
        string jsonPath)
        => sourceMap.FirstOrDefault(entry =>
            string.Equals(entry.JsonPath, jsonPath, StringComparison.Ordinal))?.RendererTemplatePath;

    private static string GetTarget(string code)
        => code is "RendererTemplateMissing" or "RendererTokenMismatch"
            ? "renderer-template"
            : "blueprint";

    private static string GetRepairKind(string code)
        => code switch
        {
            "PackNotFound" or "PackNotDeclared" or "PackIdMissing" or "PrimaryPackNotDeclared" => "import-pack",
            "OptionalPackMissing" or "ExplicitPackVersionRequired" or "PackVersionConflict" or "PackVersionMismatch" => "import-pack",
            "UnknownBlockKind" or "UnqualifiedBlockKind" => "choose-catalog-block",
            "SlotChildKindNotAllowed" => "replace-child-kind",
            "RequiredPropertyMissing" => "add-property",
            "RendererTemplateMissing" or "RendererTokenMismatch" => "fix-renderer-template",
            _ => "review-blueprint"
        };

    private static string GetSuggestedAction(BlueprintValidationIssue issue, string target)
        => target == "renderer-template"
            ? $"{issue.RepairSuggestion} Report this as a pack renderer template issue if the blueprint is valid."
            : $"{issue.RepairSuggestion} Prefer updating the blueprint before editing generated XAML.";

    private JsonElement? GetSuggestedValue(BlueprintValidationIssue issue, string blueprintJson)
    {
        if (issue.Code != "RequiredPropertyMissing")
        {
            return null;
        }

        var propertyName = GetPropertyName(issue.JsonPath);
        if (propertyName is null)
        {
            return null;
        }

        try
        {
            var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
                blueprintJson,
                "<inline-blueprint>",
                UiComposerSchemaVersions.UiBlueprint);
            var node = FindNode(blueprint.Layout, issue.JsonPath);
            var block = node is null ? null : FindBlock(blueprint, node.Kind);
            if (block?.Properties.TryGetValue(propertyName, out var property) != true)
            {
                return null;
            }

            return property!.Default is JsonElement defaultValue
                ? defaultValue.Clone()
                : FallbackValue(property.Type);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            return null;
        }
    }

    private UiBlockDefinition? FindBlock(UiBlueprint blueprint, string kind)
    {
        var packId = ComposerPackKindResolver.ResolveDeclaredPackId(kind, blueprint.Packs.Select(pack => pack.Id))
            ?? ComposerPackKindResolver.GetFallbackPackId(kind);
        var packReference = blueprint.Packs.FirstOrDefault(pack =>
            string.Equals(pack.Id, packId, StringComparison.Ordinal));
        if (packReference is null)
        {
            return null;
        }

        var pack = registry.ListPacks().Packs.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, packReference.Id, StringComparison.Ordinal)
            && string.Equals(candidate.Version, packReference.Version, StringComparison.Ordinal));
        return pack is null
            ? null
            : ComposerPackLoader.Load(pack.RootPath).Blocks.FirstOrDefault(block =>
                string.Equals(block.Kind, kind, StringComparison.Ordinal));
    }

    private static UiBlueprintNode? FindNode(UiBlueprintNode root, string issuePath)
    {
        const string layoutPrefix = "$.layout";
        var propertyIndex = issuePath.IndexOf(".properties.", StringComparison.Ordinal);
        var nodePath = propertyIndex < 0 ? issuePath : issuePath[..propertyIndex];
        if (!nodePath.StartsWith(layoutPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var node = root;
        foreach (var segment in nodePath[layoutPrefix.Length..]
                     .Split(".slots.", StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            var close = segment.IndexOf(']', StringComparison.Ordinal);
            if (bracket <= 0
                || close <= bracket
                || !int.TryParse(segment[(bracket + 1)..close], out var index)
                || !node.Slots.TryGetValue(segment[..bracket], out var children)
                || index < 0
                || index >= children.Length)
            {
                return null;
            }

            node = children[index];
        }

        return node;
    }

    private static string? GetPropertyName(string jsonPath)
    {
        const string marker = ".properties.";
        var index = jsonPath.LastIndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? null : jsonPath[(index + marker.Length)..];
    }

    private static JsonElement FallbackValue(string type)
        => type switch
        {
            "boolean" or "bool" => JsonSerializer.SerializeToElement(false),
            "number" => JsonSerializer.SerializeToElement(0),
            "object" => JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
            _ => JsonSerializer.SerializeToElement(string.Empty)
        };

    private static IEnumerable<BlueprintRepairAction> ParseDiagnostics(string? diagnosticsJson)
    {
        if (string.IsNullOrWhiteSpace(diagnosticsJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(diagnosticsJson);
            var diagnostics = GetDiagnosticElements(document.RootElement);
            return diagnostics.Select(ToDiagnosticAction).ToArray();
        }
        catch (JsonException)
        {
            return
            [
                new(
                    "diagnostic",
                    "blueprint",
                    "review-diagnostic",
                    "InvalidDiagnosticsJson",
                    "$",
                    "Diagnostics JSON could not be parsed.",
                    "Provide preview or renderer diagnostics as a JSON object or array.",
                    [],
                    [],
                    null,
                    null)
            ];
        }
    }

    private static JsonElement[] GetDiagnosticElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToArray();
        }

        if (TryGetProperty(root, "structuredContent", out var structuredContent))
        {
            return GetDiagnosticElements(structuredContent);
        }

        if (TryGetProperty(root, "result", out var result))
        {
            return GetDiagnosticElements(result);
        }

        if (TryGetProperty(root, "diagnostics", out var diagnostics)
            && diagnostics.ValueKind == JsonValueKind.Array)
        {
            return diagnostics.EnumerateArray().ToArray();
        }

        if (TryGetProperty(root, "errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array)
        {
            return errors.EnumerateArray().ToArray();
        }

        return [root];
    }

    private static BlueprintRepairAction ToDiagnosticAction(JsonElement diagnostic)
    {
        var code = GetString(diagnostic, "code") ?? "Diagnostic";
        var message = GetString(diagnostic, "message") ?? diagnostic.GetRawText();
        var jsonPath = GetString(diagnostic, "jsonPath") ?? "$.layout";
        var rendererTemplatePath = GetString(diagnostic, "rendererTemplatePath");
        var text = code + " " + message;

        var (target, kind, action) = text switch
        {
            var value when value.Contains("overflow", StringComparison.OrdinalIgnoreCase) =>
                ("blueprint", "adjust-container-spacing", "Use a container block or spacing token that fits the preview layout."),
            var value when value.Contains("empty", StringComparison.OrdinalIgnoreCase) =>
                ("blueprint", "add-slot-child", "Add the missing slot child or remove the empty container."),
            var value when value.Contains("command", StringComparison.OrdinalIgnoreCase)
                           && value.Contains("binding", StringComparison.OrdinalIgnoreCase) =>
                ("blueprint", "add-viewmodel-contract", "Declare the expected ViewModel command binding contract."),
            var value when value.Contains("token", StringComparison.OrdinalIgnoreCase) =>
                ("renderer-template", "fix-renderer-template", "Report the renderer token and matching block property contract."),
            _ => ("blueprint", "fix-blueprint-renderer-contract", "Review the block properties and slots before editing generated XAML.")
        };

        return new BlueprintRepairAction(
            "diagnostic",
            target,
            kind,
            code,
            jsonPath,
            message,
            action,
            [],
            [],
            null,
            rendererTemplatePath);
    }

    private static string? GetString(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }
}

internal sealed record BlueprintRepairRequest(
    string BlueprintJson,
    string? DiagnosticsJson = null,
    string? TargetPath = null);

internal sealed record BlueprintRepairResult(
    bool Success,
    bool Repairable,
    bool GeneratedXamlPatch,
    int ActionCount,
    IReadOnlyList<BlueprintRepairAction> Actions,
    IReadOnlyList<string> Diagnostics);

internal sealed record BlueprintRepairAction(
    string Source,
    string Target,
    string RepairKind,
    string IssueCode,
    string JsonPath,
    string Message,
    string SuggestedAction,
    IReadOnlyList<string> AllowedKinds,
    IReadOnlyList<string> AllowedValues,
    JsonElement? SuggestedValue,
    string? RendererTemplatePath)
{
    public IReadOnlyList<string> Sources { get; init; } = [Source];
}
