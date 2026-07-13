using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    private static object ValidateBlueprint(string blueprintJson, string? projectRoot, string? localAppDataRoot)
    {
        var validator = new BlueprintValidationService(CreateRegistry(projectRoot, localAppDataRoot));
        var result = validator.Validate(blueprintJson);

        return new
        {
            success = true,
            valid = result.Success,
            errorCount = result.Errors.Count,
            warningCount = result.Warnings.Count,
            errors = result.Errors,
            warnings = result.Warnings,
            resolution = result.Resolution,
            blueprintSize = new
            {
                currentCharacters = blueprintJson.Length,
                maximumCharacters = BoundaryStringLimits.MaxStringifiedJsonArgumentLength,
                remainingCharacters = BoundaryStringLimits.MaxStringifiedJsonArgumentLength - blueprintJson.Length,
                utilizationPercent = blueprintJson.Length * 100d / BoundaryStringLimits.MaxStringifiedJsonArgumentLength
            },
            diagnostics = result.Diagnostics,
            observability = ComposerObservability.ForBlueprintValidation(result)
        };
    }
}
