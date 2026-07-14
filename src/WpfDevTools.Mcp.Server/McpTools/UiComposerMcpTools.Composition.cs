using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    [McpServerTool(Name = "compose_ui_blueprint", Title = "Compose UI Blueprint Block", OpenWorld = false, ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ComposeUiBlueprint)]
    public static Task<CallToolResult> ComposeUiBlueprint(
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("Current UI blueprint JSON text or opaque draftRef. Raw JSON returns a new object; draft input returns a new immutable derived draftRef.")] string blueprintJson,
        [StringLength(BoundaryStringLimits.MaxStringArgumentLength)]
        [Description("Target slot: $.layout.slots.content or @Panel.slots.actions.")] string targetPath,
        [StringLength(BoundaryStringLimits.MaxLabelLength)]
        [Description("Exact pack-qualified block kind to insert. Its compositionSkeleton is resolved from the installed pack.")] string kind,
        [StringLength(BoundaryStringLimits.MaxLabelLength)]
        [Description("Optional standard WPF x:Name identity for the inserted node. Must be unique in the blueprint.")] string? elementName = null,
        [StringLength(BoundaryStringLimits.MaxLabelLength)]
        [Description("Optional stable automation identity for the inserted node. Must be unique in the blueprint.")] string? automationId = null,
        [Description("Optional JSON object of pack-defined property values to apply while inserting the block. Values are validated against the installed block contract.")] JsonElement? properties = null,
        [Description("Optional zero-based insertion index. Omit to append to the target slot.")] int? insertionIndex = null,
        [Description("Optional local WPF project root used for project-local pack discovery.")] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global pack discovery.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("targetPath", targetPath),
            ("kind", kind),
            ("elementName", elementName),
            ("automationId", automationId),
            ("properties", properties),
            ("insertionIndex", insertionIndex),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(Compose(
                blueprintJson,
                targetPath,
                kind,
                elementName,
                automationId,
                properties,
                insertionIndex,
                projectRoot,
                localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    private static object Compose(
        string blueprintJson,
        string targetPath,
        string kind,
        string? elementName,
        string? automationId,
        JsonElement? properties,
        int? insertionIndex,
        string? projectRoot,
        string? localAppDataRoot)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        var result = new BlueprintCompositionService(CreateRegistry(projectRoot, localAppDataRoot))
            .Compose(input.BlueprintJson, targetPath, kind, elementName, automationId, properties, insertionIndex);
        var validation = result.Validation is null
            ? null
            : new
            {
                valid = result.Validation.Success,
                errors = result.Validation.Errors,
                warnings = result.Validation.Warnings,
                resolution = result.Validation.Resolution,
                diagnostics = result.Validation.Diagnostics
            };

        object errors = result.Errors.Count > 0
            ? result.Errors
            : result.Validation?.Errors ?? [];
        var observability = ComposerObservability.ForComposition(result);

        if (result.Composed && input.IsDraft)
        {
            var derived = BlueprintInputResolver.Store.Create(result.BlueprintJson!);
            return derived.Success
                ? new
                {
                    success = true,
                    composed = true,
                    draftDerived = true,
                    sourceDraftRef = input.DraftRef,
                    derived.DraftRef,
                    derived.CharacterCount,
                    derived.ExpiresAt,
                    insertedPath = result.InsertedPath,
                    insertedNodeSummary = result.InsertedNodeSummary,
                    validation,
                    errors = result.Errors,
                    observability
                }
                : BlueprintDraftError(derived.Error!);
        }

        if (!result.Composed && input.IsDraft && result.CandidateBlueprintJson is not null)
        {
            var candidate = BlueprintInputResolver.Store.Create(result.CandidateBlueprintJson);
            return candidate.Success
                ? new
                {
                    success = false,
                    composed = false,
                    draftDerived = false,
                    sourceDraftRef = input.DraftRef,
                    candidateDraftRef = candidate.DraftRef,
                    candidateDraftCreated = true,
                    candidateWritten = false,
                    validation,
                    errors,
                    observability
                }
                : BlueprintDraftError(candidate.Error!);
        }

        return result.Composed
            ? new
            {
                success = true,
                composed = true,
                blueprint = result.Blueprint,
                blueprintJson = result.BlueprintJson,
                insertedPath = result.InsertedPath,
                insertedNodeSummary = result.InsertedNodeSummary,
                validation,
                errors = result.Errors,
                observability
            }
            : (object)new
            {
                success = false,
                composed = false,
                invalidCandidate = result.InvalidCandidate,
                candidateBlueprintJson = result.CandidateBlueprintJson,
                candidateWritten = false,
                validation,
                errors,
                observability
            };
    }
}
