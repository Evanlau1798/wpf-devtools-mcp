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
        new("windowChrome", "Preview uses the available runtime-backed or structural window representation.", "The final app adds project and operating-system integration.", "Confirm window chrome in the final applied app."),
        new("icons", "Preview shows package icons only where runtime dependencies are approved; stub-backed areas are structural.", "The final app uses the declared integration plan.", "Confirm every required icon in the final applied app."),
        new("controlTemplates", "Preview loads approved package templates and resources; stub-backed areas do not prove final styling.", "The final app uses the declared integration plan.", "Confirm control styling and states in the final applied app."),
        new("layoutAndSpacing", "Preview measures runtime-backed controls and structural stubs in an isolated host.", "The final app may add project-level layout context.", "Confirm layout, clipping, and spacing in the final applied app.")
    ];
    private static readonly PreviewVisualComparison[] UnavailableVisualComparisonItems =
        VisualComparisonItems.Select(item => item with
        {
            Preview = "No preview visual evidence is available for this result.",
            RequiredAction = "Resolve preview diagnostics, load the host, then compare this area in the final applied app."
        }).ToArray();
    private static readonly PreviewVisualComparison[] CompileOnlyVisualComparisonItems =
        VisualComparisonItems.Select(item => item with
        {
            Preview = "The preview project compiled with its selected runtime or structural configuration; the host was not started, so this is not visual evidence.",
            RequiredAction = "Start the preview host or verify this area in the final applied app."
        }).ToArray();

    public bool UsesStructuralStubs { get; init; }
    public bool UsesRuntimeDependencies { get; init; }

    public string VisualFidelity => !Valid || !BuildSucceeded || !HasUsableHostEvidence
        ? "not-available"
        : UsesStructuralStubs
            ? UsesRuntimeDependencies ? "hybrid-resource-backed" : "structural"
            : "resource-backed";

    public string VisualValidationGuidance => PreviewHost.Status == "compiled"
        ? VisualFidelity switch
        {
            "structural" => "The preview project compiled with structural metadata only, but the host was not started; no pixel evidence is available. Validate the applied, built, and launched WPF application.",
            "hybrid-resource-backed" => "The preview project compiled with approved runtime resources and structural areas, but the host was not started; no pixel evidence is available. Validate the final application.",
            _ => "The preview project compiled with approved runtime packages and resources, but the host was not started; no pixel evidence is available. Validate the final application."
        }
        : VisualFidelity switch
        {
            "not-available" => "No visual fidelity is available because the preview did not build or its requested host did not load. Resolve diagnostics before evaluating pixels.",
            "structural" => "Preview uses structural metadata only. Validate all styling in the applied, built, and launched WPF application.",
            "hybrid-resource-backed" => "Preview loads declared runtime packages and resources approved for preview while stub-backed areas remain structural. Validate final styling in the applied, built, and launched WPF application.",
            _ => "Preview loads declared runtime packages and ordered application resources approved for preview. Confirm final styling in the applied, built, and launched WPF application."
        };

    public string ScreenshotVerificationGuidance =>
        "If a client displays sparse pixels while semantic evidence is complete, re-read the same screenshot resource and verify its SHA-256 before regenerating the preview or reporting a product defect.";

    public IReadOnlyList<PreviewVisualComparison> VisualComparisonChecklist =>
        VisualFidelity == "not-available"
            ? UnavailableVisualComparisonItems
            : PreviewHost.Status == "compiled" ? CompileOnlyVisualComparisonItems : VisualComparisonItems;

    private bool HasUsableHostEvidence =>
        PreviewHost.Status is "compiled" or "loaded";

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

    public int UnresolvedCorrelationCount { get; init; }

    public int ReportedUnresolvedCorrelationCount { get; init; }

    public bool UnresolvedCorrelationsTruncated { get; init; }

    public IReadOnlyList<PreviewUnresolvedCorrelation> UnresolvedCorrelations { get; init; } = [];

    public int UninspectedCorrelationCount { get; init; }

    public int ReportedUninspectedCorrelationCount { get; init; }

    public bool UninspectedCorrelationsTruncated { get; init; }

    public IReadOnlyList<PreviewUninspectedCorrelation> UninspectedCorrelations { get; init; } = [];

    public static PreviewLayoutRiskSummary Empty { get; } = new(0, 0, false, []);
}

internal sealed record PreviewUnresolvedCorrelation(
    string JsonPath,
    string BlockKind,
    string ElementName);

internal sealed record PreviewUninspectedCorrelation(
    string JsonPath,
    string BlockKind,
    string ElementName,
    string ElementId);

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
