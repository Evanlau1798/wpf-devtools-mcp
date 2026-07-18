using System.Text.Json;
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
        bool compactRuntimeDiagnostics,
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
        var compactSuccessfulPayload = compactRuntimeDiagnostics
            && result.Success
            && result.Valid
            && result.BuildSucceeded;
        var compactElementCorrelations = ShouldCompactElementCorrelations(
            compactSuccessfulPayload,
            result.LayoutRiskSummary);

        return new
        {
            result.Success,
            result.Valid,
            blueprintDraftRef = input.IsDraft ? input.DraftRef : null,
            result.BuildSucceeded,
            result.RestoreEnabled,
            result.BuildOutput,
            xaml = compactSuccessfulPayload ? null : result.Xaml,
            generatedXamlOmitted = compactSuccessfulPayload && !string.IsNullOrEmpty(result.Xaml),
            generatedXamlLength = result.Xaml?.Length ?? 0,
            result.Diagnostics,
            previewHost = BuildPreviewHostPayload(result.PreviewHost, compactRuntimeDiagnostics),
            runtimeDiagnosticsCompacted = compactRuntimeDiagnostics
                && result.PreviewHost.RuntimeDiagnostics is not null,
            result.VisualFidelity,
            result.VisualValidationGuidance,
            result.ScreenshotVerificationGuidance,
            result.VisualComparisonChecklist,
            result.PropertyWarnings,
            elementCorrelations = compactElementCorrelations ? [] : result.ElementCorrelations,
            elementCorrelationsCompacted = compactElementCorrelations && result.ElementCorrelations.Count > 0,
            elementCorrelationCount = result.ElementCorrelations.Count,
            result.LayoutRiskSummary,
            result.RuntimePackApprovalReviews,
            observability = ComposerObservability.ForPreview(result)
        };
    }

    internal static bool ShouldCompactElementCorrelations(
        bool compactSuccessfulPayload,
        PreviewLayoutRiskSummary layoutRiskSummary)
        => compactSuccessfulPayload
           && layoutRiskSummary.ClippedElementCount == 0
           && layoutRiskSummary.UnresolvedCorrelationCount == 0
           && layoutRiskSummary.UninspectedCorrelationCount == 0
           && !layoutRiskSummary.InspectionTruncated;

    internal static object BuildPreviewHostPayload(
        PreviewHostResult host,
        bool compactRuntimeDiagnostics)
    {
        if (!compactRuntimeDiagnostics || host.RuntimeDiagnostics is null)
        {
            return host;
        }

        return new
        {
            status = host.Status,
            started = host.Started,
            viewLoaded = host.ViewLoaded,
            processId = host.ProcessId,
            runtimeDiagnosticsCompacted = true,
            fullPayloadHint = "Set compactRuntimeDiagnostics=false only when full non-screenshot diagnostic payloads are required.",
            runtimeDiagnostics = host.RuntimeDiagnostics.Select(ToCompactRuntimeDiagnostic).ToArray()
        };
    }

    private static object ToCompactRuntimeDiagnostic(PreviewRuntimeDiagnostic diagnostic)
    {
        var compact = new Dictionary<string, object?>
        {
            ["tool"] = diagnostic.Tool,
            ["success"] = diagnostic.Success
        };
        if (diagnostic.Lookup is not null)
        {
            compact["lookup"] = new
            {
                query = diagnostic.Lookup.Query,
                matchMode = diagnostic.Lookup.MatchMode
            };
        }

        if (diagnostic.TargetElementIds.Count > 0)
        {
            compact["targetElementCount"] = diagnostic.TargetElementIds.Count;
        }

        if (diagnostic.Payload.ValueKind == JsonValueKind.Object
            && diagnostic.Payload.TryGetProperty("results", out var results)
            && results.ValueKind == JsonValueKind.Array)
        {
            compact["matchedElementCount"] = results.GetArrayLength();
        }

        var keepPayload = !diagnostic.Success
            || string.Equals(diagnostic.Tool, "element_screenshot", StringComparison.Ordinal);
        compact["payloadOmitted"] = !keepPayload;
        if (keepPayload)
        {
            compact["payload"] = diagnostic.Payload;
        }

        return compact;
    }
}
