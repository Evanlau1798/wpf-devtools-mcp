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
        int? viewportWidth,
        int? viewportHeight,
        string[]? runtimePackApprovalTokens,
        int correlationLookupLimit,
        string? projectRoot,
        string? localAppDataRoot,
        CancellationToken cancellationToken)
    {
        if (runtimePackApprovalTokens is { Length: > UiPreviewRuntimeDependencyPolicy.MaximumCallApprovalTokens })
        {
            return BatchItemLimits.CreateInvalidArgumentError(
                "runtimePackApprovalTokens",
                runtimePackApprovalTokens.Length,
                UiPreviewRuntimeDependencyPolicy.MaximumCallApprovalTokens,
                $"runtimePackApprovalTokens accepts at most {UiPreviewRuntimeDependencyPolicy.MaximumCallApprovalTokens} items.",
                "Pass only exact-content tokens for packs used by this preview call.");
        }

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

        var viewportArgs = ToolCallHelper.BuildJsonArgs(
            ("viewportWidth", viewportWidth),
            ("viewportHeight", viewportHeight));
        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
                viewportArgs,
                "viewportWidth",
                1,
                UiPreviewProjectFiles.MaximumViewportDimension,
                out var resolvedViewportWidth,
                out var viewportWidthError))
        {
            return viewportWidthError!;
        }

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
                viewportArgs,
                "viewportHeight",
                1,
                UiPreviewProjectFiles.MaximumViewportDimension,
                out var resolvedViewportHeight,
                out var viewportHeightError))
        {
            return viewportHeightError!;
        }

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
                ToolCallHelper.BuildJsonArgs(("correlationLookupLimit", correlationLookupLimit)),
                "correlationLookupLimit",
                1,
                UiBlueprintPreviewDiagnosticsBridge.MaximumNameLookupLimit,
                out var resolvedCorrelationLookupLimit,
                out var correlationLookupLimitError))
        {
            return correlationLookupLimitError!;
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
                    ScreenshotMaxHeight: resolvedScreenshotMaxHeight,
                    CorrelationLookupLimit: resolvedCorrelationLookupLimit!.Value,
                    ViewportWidth: resolvedViewportWidth,
                    ViewportHeight: resolvedViewportHeight,
                    RuntimePackApprovalTokens: runtimePackApprovalTokens),
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
