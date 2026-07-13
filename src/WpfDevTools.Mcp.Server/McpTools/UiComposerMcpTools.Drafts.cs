using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
        [Description("JSON Merge Patch object. Null removes a property; objects merge recursively; arrays and scalars replace their target value.")] string patchJson,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(("draftRef", draftRef), ("patchJson", patchJson));
        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult(PatchDraft(draftRef, patchJson)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    private static object CreateDraft(string blueprintJson)
        => DraftMutationPayload(BlueprintInputResolver.Store.Create(blueprintJson), sourceDraftRef: null);

    private static object PatchDraft(string draftRef, string patchJson)
    {
        var result = BlueprintInputResolver.Store.ApplyMergePatch(draftRef, patchJson);
        if (result.Success)
        {
            return DraftMutationPayload(result, draftRef);
        }

        var jsonPath = result.Error!.Code == "BlueprintDraftNotFound"
            ? "$.draftRef"
            : "$.patchJson";
        return BlueprintDraftError(result.Error, jsonPath);
    }

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
