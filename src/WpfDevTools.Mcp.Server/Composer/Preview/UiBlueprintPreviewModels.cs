using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed record PreviewBlueprintRequest(
    string BlueprintJson,
    bool RestoreEnabled = true,
    bool KeepArtifacts = false,
    string? TemporaryRoot = null,
    bool StartHost = false,
    bool IncludeRuntimeDiagnostics = false,
    bool IncludeScreenshotDiagnostics = false,
    string ScreenshotOutputMode = "metadata",
    int? ScreenshotMaxWidth = 1024,
    int? ScreenshotMaxHeight = 1024)
{
    public IReadOnlyList<RenderElementCorrelation> RuntimeElementCorrelations { get; init; } = [];
}

internal sealed record DotnetCommandResult(bool Succeeded, bool Cancelled);

internal sealed record PreviewBlueprintResult(
    bool Success,
    bool Valid,
    bool BuildSucceeded,
    bool RestoreEnabled,
    string BuildOutput,
    string Xaml,
    IReadOnlyList<PreviewDiagnostic> Diagnostics,
    PreviewHostResult PreviewHost)
{
    private static readonly PreviewVisualComparison[] VisualComparisonItems =
    [
        new("windowChrome", "Temporary preview window chrome is structural.", "The applied app uses its real window type, title bar, and system buttons.", "Inspect window chrome in the final applied app."),
        new("icons", "Stub controls may omit or substitute pack icons.", "The applied app renders icons from the real UI package.", "Inspect every required icon in the final applied app."),
        new("controlTemplates", "Stub control templates approximate styling and visual states.", "The applied app uses the real package templates, resources, and states.", "Inspect control styling and states in the final applied app."),
        new("layoutAndSpacing", "Stub measurement can shift sizing, alignment, and spacing.", "The applied app uses the real package measure and arrange behavior.", "Inspect layout, clipping, and spacing in the final applied app.")
    ];

    public string VisualFidelity => "structural-stub";

    public string VisualValidationGuidance =>
        "Use preview screenshots for structural diagnostics only. Validate final styling in the applied, built, and launched WPF application.";

    public string ScreenshotVerificationGuidance =>
        "If a client displays sparse pixels while semantic evidence is complete, re-read the same screenshot resource and verify its SHA-256 before regenerating the preview or reporting a product defect.";

    public IReadOnlyList<PreviewVisualComparison> VisualComparisonChecklist => VisualComparisonItems;

    public IReadOnlyList<PreviewPropertyWarning> PropertyWarnings { get; init; } = [];

    public IReadOnlyList<RenderElementCorrelation> ElementCorrelations { get; init; } = [];

    public PreviewLayoutRiskSummary LayoutRiskSummary { get; init; } = PreviewLayoutRiskSummary.Empty;

    public static PreviewBlueprintResult Invalid(
        bool restoreEnabled,
        string xaml,
        IReadOnlyList<PreviewDiagnostic> diagnostics)
        => new(false, false, false, restoreEnabled, string.Empty, xaml, diagnostics, new PreviewHostResult("not-started", Started: false));
}

internal sealed record PreviewVisualComparison(
    string Area,
    string Preview,
    string FinalApp,
    string RequiredAction);

internal sealed record PreviewPropertyWarning(
    string JsonPath,
    string BlockKind,
    string PropertyName,
    string Message);

internal sealed record PreviewDiagnostic(
    string Code,
    string Message,
    string JsonPath,
    string RendererTemplatePath)
{
    public IReadOnlyList<string> RelatedJsonPaths { get; init; } = [];
}

internal sealed record PreviewHostResult(
    string Status,
    bool Started,
    bool ViewLoaded = false,
    int? ProcessId = null,
    IReadOnlyList<PreviewRuntimeDiagnostic>? RuntimeDiagnostics = null);

internal sealed record PreviewRuntimeDiagnostic(
    string Tool,
    bool Success,
    JsonElement Payload)
{
    public IReadOnlyList<string> TargetElementIds { get; init; } = [];
}

internal sealed record PreviewLayoutRiskSummary(
    int ClippedElementCount,
    int ReportedWarningCount,
    bool WarningsTruncated,
    IReadOnlyList<PreviewLayoutWarning> Warnings)
{
    public int CorrelatedTargetCount { get; init; }

    public int ResolvedTargetCount { get; init; }

    public int InspectedTargetCount { get; init; }

    public bool InspectionTruncated { get; init; }

    public static PreviewLayoutRiskSummary Empty { get; } = new(0, 0, false, []);
}

internal sealed record PreviewLayoutWarning(
    string Code,
    string JsonPath,
    string BlockKind,
    string ElementName,
    string ElementId,
    string ClippingSource,
    JsonElement OverflowAmount,
    string? SuggestedFix);

internal sealed record PreviewCorrelationLookup(string Query, string MatchMode);
