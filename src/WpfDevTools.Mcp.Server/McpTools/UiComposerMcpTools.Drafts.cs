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
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("draftRef", draftRef),
            ("patchJson", patchJson),
            ("jsonPath", jsonPath),
            ("value", value),
            ("remove", remove));
        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult(PatchDraft(draftRef, patchJson, jsonPath, value, remove)),
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
        bool remove)
    {
        if ((patchJson is null) == (jsonPath is null))
        {
            var issue = new BlueprintDraftIssue(
                patchJson is null
                    ? "BlueprintDraftMutationModeRequired"
                    : "BlueprintDraftMutationModeConflict",
                patchJson is null
                    ? "Pass either patchJson or jsonPath mutation arguments."
                    : "patchJson and jsonPath mutation modes cannot be combined.",
                "Use patchJson for JSON Merge Patch, or jsonPath with value/remove for one surgical change.");
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

        var result = patchJson is not null
            ? BlueprintInputResolver.Store.ApplyMergePatch(draftRef, patchJson)
            : BlueprintInputResolver.Store.ApplyPathUpdate(draftRef, jsonPath!, value, remove);
        if (result.Success)
        {
            return DraftMutationPayload(result, draftRef);
        }

        var errorPath = result.Error!.Code switch
        {
            "BlueprintDraftNotFound" => "$.draftRef",
            "InvalidBlueprintDraftPath" or "BlueprintDraftPathNotFound" => "$.jsonPath",
            "BlueprintDraftValueRequired" or "BlueprintDraftRemoveValueConflict" => "$.value",
            "BlueprintDraftTooLarge" when patchJson is null => "$.value",
            _ => patchJson is not null ? "$.patchJson" : "$.jsonPath"
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
