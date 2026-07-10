using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed record PreviewBlueprintRequest(
    string BlueprintJson,
    bool RestoreEnabled = true,
    bool KeepArtifacts = false,
    string? TemporaryRoot = null,
    bool StartHost = false,
    bool IncludeRuntimeDiagnostics = false,
    bool IncludeScreenshotDiagnostics = false,
    string ScreenshotOutputMode = "metadata");

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
    public string VisualFidelity => "structural-stub";

    public string VisualValidationGuidance =>
        "Use preview screenshots for structural diagnostics only. Validate final styling in the applied, built, and launched WPF application.";

    public static PreviewBlueprintResult Invalid(
        bool restoreEnabled,
        string xaml,
        IReadOnlyList<PreviewDiagnostic> diagnostics)
        => new(false, false, false, restoreEnabled, string.Empty, xaml, diagnostics, new PreviewHostResult("not-started", Started: false));
}

internal sealed record PreviewDiagnostic(
    string Code,
    string Message,
    string JsonPath,
    string RendererTemplatePath);

internal sealed record PreviewHostResult(
    string Status,
    bool Started,
    bool ViewLoaded = false,
    int? ProcessId = null,
    IReadOnlyList<PreviewRuntimeDiagnostic>? RuntimeDiagnostics = null);

internal sealed record PreviewRuntimeDiagnostic(
    string Tool,
    bool Success,
    JsonElement Payload);
