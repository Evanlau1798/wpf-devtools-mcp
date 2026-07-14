using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    private static async Task<object> PreviewBlueprint(
        SessionManager sessionManager,
        string blueprintJson,
        bool restoreEnabled,
        bool startHost,
        bool includeRuntimeDiagnostics,
        bool includeScreenshotDiagnostics,
        string screenshotOutputMode,
        int? screenshotMaxWidth,
        int? screenshotMaxHeight,
        string? projectRoot,
        string? localAppDataRoot,
        CancellationToken cancellationToken)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        if (!BoundaryParameterValidator.TryGetOptionalStringEnum(
            ToolCallHelper.BuildJsonArgs(("screenshotOutputMode", screenshotOutputMode)),
            "screenshotOutputMode",
            "metadata",
            ["metadata", "file"],
            out var resolvedScreenshotOutputMode,
            out var screenshotOutputModeError))
        {
            return screenshotOutputModeError!;
        }

        var screenshotSizeArgs = ToolCallHelper.BuildJsonArgs(
            ("screenshotMaxWidth", screenshotMaxWidth),
            ("screenshotMaxHeight", screenshotMaxHeight));
        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
                screenshotSizeArgs,
                "screenshotMaxWidth",
                1,
                int.MaxValue,
                out var resolvedScreenshotMaxWidth,
                out var screenshotMaxWidthError))
        {
            return screenshotMaxWidthError!;
        }

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
                screenshotSizeArgs,
                "screenshotMaxHeight",
                1,
                int.MaxValue,
                out var resolvedScreenshotMaxHeight,
                out var screenshotMaxHeightError))
        {
            return screenshotMaxHeightError!;
        }

        var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot, localAppDataRoot), sessionManager)
            .PreviewAsync(
                new PreviewBlueprintRequest(
                    input.BlueprintJson,
                    restoreEnabled,
                    StartHost: startHost,
                    IncludeRuntimeDiagnostics: includeRuntimeDiagnostics,
                    IncludeScreenshotDiagnostics: includeScreenshotDiagnostics,
                    ScreenshotOutputMode: resolvedScreenshotOutputMode,
                    ScreenshotMaxWidth: resolvedScreenshotMaxWidth,
                    ScreenshotMaxHeight: resolvedScreenshotMaxHeight),
                cancellationToken)
            .ConfigureAwait(false);

        return new
        {
            result.Success,
            result.Valid,
            blueprintDraftRef = input.IsDraft ? input.DraftRef : null,
            result.BuildSucceeded,
            result.RestoreEnabled,
            result.BuildOutput,
            result.Xaml,
            result.Diagnostics,
            result.PreviewHost,
            result.VisualFidelity,
            result.VisualValidationGuidance,
            result.ScreenshotVerificationGuidance,
            result.VisualComparisonChecklist,
            result.PropertyWarnings,
            result.ElementCorrelations,
            result.LayoutRiskSummary,
            observability = ComposerObservability.ForPreview(result)
        };
    }
}
