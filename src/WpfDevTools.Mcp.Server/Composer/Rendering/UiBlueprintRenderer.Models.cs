using WpfDevTools.Mcp.Server.Composer.Blueprints;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed record RenderBlueprintRequest(
    string BlueprintJson,
    string? TargetPath = null,
    string? ProjectRoot = null,
    bool IncludeTransientElementCorrelation = false);

internal sealed record RenderBlueprintResult(
    bool Success,
    bool Valid,
    bool DryRun,
    string Xaml,
    RenderFilePlan FilePlan,
    IReadOnlyList<string> RequiredResources,
    IReadOnlyList<RequiredNuGetPackage> RequiredNuGetPackages,
    BlueprintValidationResult Validation,
    IReadOnlyList<BlueprintValidationIssue> Errors,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<RenderSourceMapEntry> SourceMap,
    IReadOnlyList<RenderElementCorrelation> ElementCorrelations)
{
    public static RenderBlueprintResult Invalid(
        RenderBlueprintRequest request,
        BlueprintValidationResult validation,
        IReadOnlyList<BlueprintValidationIssue> errors)
        => new(
            Success: false,
            Valid: false,
            DryRun: true,
            Xaml: string.Empty,
            FilePlan: new RenderFilePlan(
                RenderTargetPath.Resolve(request, request.TargetPath ?? Path.Combine("Views", "GeneratedView.xaml")),
                WouldWriteFiles: false),
            RequiredResources: [],
            RequiredNuGetPackages: [],
            Validation: validation,
            Errors: errors,
            Diagnostics: validation.Diagnostics,
            SourceMap: [],
            ElementCorrelations: []);
}

internal sealed record RenderFilePlan(string TargetPath, bool WouldWriteFiles);

internal static class RenderTargetPath
{
    public static string Resolve(RenderBlueprintRequest request, string targetPath)
        => Path.GetFullPath(!Path.IsPathFullyQualified(targetPath) && !string.IsNullOrWhiteSpace(request.ProjectRoot)
            ? Path.Combine(request.ProjectRoot, targetPath)
            : targetPath);
}

internal sealed record RequiredNuGetPackage(string Id, string VersionRange);

internal sealed record RenderSourceMapEntry(
    string JsonPath,
    string BlockKind,
    string RendererTemplatePath,
    string Xaml,
    int StartIndex = -1,
    int EndIndex = -1,
    int StartLine = 0,
    int StartColumn = 0,
    int EndLine = 0,
    int EndColumn = 0);

internal sealed record RenderElementCorrelation(
    string ElementName,
    string JsonPath,
    string BlockKind);
