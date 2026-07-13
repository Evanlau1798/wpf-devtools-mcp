using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    [McpServerTool(Name = "compose_ui_blueprint", Title = "Compose UI Blueprint Block", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ComposeUiBlueprint)]
    public static Task<CallToolResult> ComposeUiBlueprint(
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("Current UI blueprint JSON text. The tool returns a new object and never writes files.")] string blueprintJson,
        [StringLength(BoundaryStringLimits.MaxStringArgumentLength)]
        [Description("Exact target slot path, such as $.layout.slots.content or $.layout.slots.content[0].slots.actions.")] string targetPath,
        [StringLength(BoundaryStringLimits.MaxLabelLength)]
        [Description("Exact pack-qualified block kind to insert. Its compositionSkeleton is resolved from the installed pack.")] string kind,
        [Description("Optional zero-based insertion index. Omit to append to the target slot.")] int? insertionIndex = null,
        [Description("Optional local WPF project root used for project-local pack discovery.")] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global pack discovery.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("targetPath", targetPath),
            ("kind", kind),
            ("insertionIndex", insertionIndex),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(Compose(
                blueprintJson,
                targetPath,
                kind,
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
        int? insertionIndex,
        string? projectRoot,
        string? localAppDataRoot)
    {
        var result = new BlueprintCompositionService(CreateRegistry(projectRoot, localAppDataRoot))
            .Compose(blueprintJson, targetPath, kind, insertionIndex);
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

        return result.Composed
            ? new
            {
                success = true,
                composed = true,
                blueprint = result.Blueprint,
                blueprintJson = result.BlueprintJson,
                insertedPath = result.InsertedPath,
                validation,
                errors = result.Errors,
                observability
            }
            : (object)new
            {
                success = true,
                composed = false,
                validation,
                errors,
                observability
            };
    }
}
