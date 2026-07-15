using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    [McpServerTool(Name = "create_ui_blueprint_draft", Title = "Create Ephemeral UI Blueprint Draft", OpenWorld = false, ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.CreateUiBlueprintDraft)]
    public static Task<CallToolResult> CreateUiBlueprintDraft(
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("UI blueprint JSON object to retain in the bounded process-local draft store.")] string blueprintJson,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(("blueprintJson", blueprintJson));
        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult(CreateDraft(blueprintJson)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "patch_ui_blueprint_draft", Title = "Derive Patched UI Blueprint Draft", OpenWorld = false, ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.PatchUiBlueprintDraft)]
    public static Task<CallToolResult> PatchUiBlueprintDraft(
        [StringLength(BoundaryStringLimits.MaxStringArgumentLength)]
        [Description("Opaque draftRef returned by create_ui_blueprint_draft or a prior derived draft operation.")] string draftRef,
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("Optional JSON Merge Patch object. Use this mode for object-wide changes; arrays and scalars replace their target value.")] string? patchJson = null,
        [StringLength(BoundaryStringLimits.MaxStringArgumentLength)]
        [Description("Set/remove target: $.layout.properties[\"accent.color\"] or @Panel.properties.text. Do not combine with patchJson.")] string? jsonPath = null,
        [Description("JSON value for jsonPath set mode. Omit only when remove=true.")] JsonElement? value = null,
        [Description("When true, removes the exact jsonPath target. Omit value and patchJson.")] bool remove = false,
        [MaxLength(BlueprintDraftPathOperation.MaxOperations)]
        [Description("Optional ordered array of up to 16 atomic set/remove operations. Do not combine with patchJson or jsonPath.")] BlueprintDraftPathOperation[]? operations = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("draftRef", draftRef),
            ("patchJson", patchJson),
            ("jsonPath", jsonPath),
            ("value", value),
            ("remove", remove),
            ("operations", operations));
        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult(PatchDraft(draftRef, patchJson, jsonPath, value, remove, operations)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    private static object CreateDraft(string blueprintJson)
        => DraftMutationPayload(BlueprintInputResolver.Store.Create(blueprintJson), sourceDraftRef: null);

    private static object PatchDraft(
        string draftRef,
        string? patchJson,
        string? jsonPath,
        JsonElement? value,
        bool remove,
        BlueprintDraftPathOperation[]? operations)
    {
        var modeCount = (patchJson is null ? 0 : 1)
                        + (jsonPath is null ? 0 : 1)
                        + (operations is null ? 0 : 1);
        if (modeCount != 1)
        {
            var issue = new BlueprintDraftIssue(
                modeCount == 0
                    ? "BlueprintDraftMutationModeRequired"
                    : "BlueprintDraftMutationModeConflict",
                modeCount == 0
                    ? "Pass patchJson, jsonPath, or operations mutation arguments."
                    : "patchJson, jsonPath, and operations mutation modes cannot be combined.",
                "Use exactly one mutation mode per call.");
            return BlueprintDraftError(issue, "$.");
        }

        if (patchJson is not null && (HasNonNullJsonValue(value) || remove))
        {
            return BlueprintDraftError(
                new BlueprintDraftIssue(
                    "BlueprintDraftMutationModeConflict",
                    "Merge Patch mode cannot include path mutation arguments.",
                    "Pass only draftRef and patchJson for JSON Merge Patch mode."),
                "$.patchJson");
        }

        if (operations is not null && (HasNonNullJsonValue(value) || remove))
        {
            return BlueprintDraftError(
                new BlueprintDraftIssue(
                    "BlueprintDraftMutationModeConflict",
                    "Atomic operations mode cannot include single-path mutation arguments.",
                    "Pass only draftRef and operations for an atomic multi-path edit."),
                "$.operations");
        }

        if (operations is { Length: 0 })
        {
            return BlueprintDraftError(
                new BlueprintDraftIssue(
                    "BlueprintDraftOperationsRequired",
                    "Atomic operations mode requires at least one operation.",
                    "Pass one to 16 ordered set/remove operations."),
                "$.operations");
        }

        if (operations is { Length: > BlueprintDraftPathOperation.MaxOperations })
        {
            return BlueprintDraftError(
                new BlueprintDraftIssue(
                    "BlueprintDraftTooManyOperations",
                    $"Atomic operations mode accepts at most {BlueprintDraftPathOperation.MaxOperations} operations.",
                    "Split the edit into bounded batches of 16 operations or fewer."),
                "$.operations");
        }

        var result = patchJson is not null
            ? BlueprintInputResolver.Store.ApplyMergePatch(draftRef, patchJson)
            : operations is not null
                ? BlueprintInputResolver.Store.ApplyPathUpdates(draftRef, operations)
                : BlueprintInputResolver.Store.ApplyPathUpdate(draftRef, jsonPath!, value, remove);
        if (result.Success)
        {
            return DraftMutationPayload(result, draftRef);
        }

        var errorPath = result.Error!.RequestJsonPath ?? result.Error.Code switch
        {
            "BlueprintDraftNotFound" => "$.draftRef",
            "InvalidBlueprintDraftPath" or "BlueprintDraftPathNotFound" => "$.jsonPath",
            "BlueprintDraftValueRequired" or "BlueprintDraftRemoveValueConflict" => "$.value",
            "BlueprintDraftTooLarge" when operations is not null => "$.operations",
            "BlueprintDraftTooLarge" when patchJson is null => "$.value",
            _ => patchJson is not null ? "$.patchJson" : operations is not null ? "$.operations" : "$.jsonPath"
        };
        return BlueprintDraftError(result.Error, errorPath);
    }

    private static bool HasNonNullJsonValue(JsonElement? value)
        => value is { } element && element.ValueKind != JsonValueKind.Null;

    private static object DraftMutationPayload(
        BlueprintDraftMutationResult result,
        string? sourceDraftRef)
        => result.Success
            ? new
            {
                success = true,
                result.DraftRef,
                sourceDraftRef,
                result.CharacterCount,
                result.ExpiresAt,
                changeSummary = result.ChangeSummary,
                immutable = true,
                retention = new
                {
                    maxDrafts = BlueprintDraftStore.DefaultMaxDrafts,
                    maxCharacters = BoundaryStringLimits.MaxStringifiedJsonArgumentLength,
                    lifetimeSeconds = (int)BlueprintDraftStore.DefaultLifetime.TotalSeconds,
                    persistence = "process-memory-only"
                }
            }
            : BlueprintDraftError(result.Error!);

    private static object BlueprintDraftError(
        BlueprintDraftIssue issue,
        string jsonPath = "$.blueprintJson")
        => new
        {
            success = false,
            errors = new[]
            {
                new
                {
                    jsonPath,
                    issue.Code,
                    issue.Message,
                    issue.RepairSuggestion
                }
            }
        };
}
