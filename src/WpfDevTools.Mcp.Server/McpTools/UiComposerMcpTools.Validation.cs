using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    private static object ValidateBlueprint(
        string blueprintJson,
        string? targetPath,
        string? projectRoot,
        string? localAppDataRoot)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        var validator = new BlueprintValidationService(CreateRegistry(projectRoot, localAppDataRoot));
        var result = validator.Validate(input.BlueprintJson, targetPath, projectRoot);

        return new
        {
            success = true,
            valid = result.Success,
            errorCount = result.Errors.Count,
            warningCount = result.Warnings.Count,
            blueprintDraftRef = input.IsDraft ? input.DraftRef : null,
            errors = result.Errors,
            warnings = result.Warnings,
            resolution = result.Resolution,
            blueprintSize = new
            {
                currentCharacters = input.BlueprintJson.Length,
                maximumCharacters = BoundaryStringLimits.MaxStringifiedJsonArgumentLength,
                remainingCharacters = BoundaryStringLimits.MaxStringifiedJsonArgumentLength - input.BlueprintJson.Length,
                utilizationPercent = input.BlueprintJson.Length * 100d / BoundaryStringLimits.MaxStringifiedJsonArgumentLength
            },
            diagnostics = result.Diagnostics,
            observability = ComposerObservability.ForBlueprintValidation(result)
        };
    }
}
