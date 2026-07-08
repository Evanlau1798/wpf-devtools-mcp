using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Diagnostics;

internal static partial class ComposerObservability
{
    private static readonly string[] ExcludedFields =
    [
        "blueprintJson",
        "generatedXaml",
        "fullUserFileContent",
        "secrets",
        "absoluteLocalPaths"
    ];

    public static ComposerObservabilityPayload ForPackList(IReadOnlyList<string> diagnostics)
        => Create(
            [new("pack_registry_log", "list_ui_block_packs", Outcome(diagnostics.Count == 0))],
            DiagnosticCounts(diagnostics.Select(_ => "PackRegistryDiagnostic")));

    public static ComposerObservabilityPayload ForCatalog(IReadOnlyList<string> diagnostics)
        => Create(
            [new("catalog_log", "get_ui_block_catalog", Outcome(diagnostics.Count == 0))],
            DiagnosticCounts(diagnostics.Select(_ => "CatalogDiagnostic")));

    public static ComposerObservabilityPayload ForBlueprintValidation(BlueprintValidationResult result)
        => Create(
            [LogFromIssue("blueprint_validation_log", "validate_ui_blueprint", Outcome(result.Success), FirstIssue(result))],
            DiagnosticCounts(result.Errors.Concat(result.Warnings).Select(issue => issue.Code)),
            blueprintValidationFailed: !result.Success);

    public static ComposerObservabilityPayload ForRecipeExpansion(RecipeExpansionResult result)
        => Create(
            [LogFromIssue("blueprint_validation_log", "expand_ui_recipe", Outcome(result.Success), FirstIssue(result))],
            DiagnosticCounts(result.Errors.Concat(result.Warnings).Select(issue => issue.Code)),
            blueprintValidationFailed: !result.Success);

    public static ComposerObservabilityPayload ForRenderDryRun(RenderBlueprintResult result)
        => Create(
            [LogFromIssue("render_dry_run_log", "render_ui_blueprint", Outcome(result.Valid), result.Errors.FirstOrDefault())],
            DiagnosticCounts(result.Errors.Select(issue => issue.Code)),
            rendererFailed: !result.Valid);

    public static ComposerObservabilityPayload ForRepair(BlueprintRepairResult result)
        => Create(
            [LogFromRepairAction("repair_log", "repair_ui_blueprint", Outcome(result.Success), result.Actions.FirstOrDefault())],
            DiagnosticCounts(result.Actions.Select(action => action.IssueCode)));

    public static ComposerObservabilityPayload ForApply(ApplyBlueprintResult result)
    {
        var logs = new List<ComposerLogEntry>
        {
            LogFromIssue("apply_plan_log", "apply_ui_blueprint", Outcome(result.Success), result.Errors.FirstOrDefault()),
            LogFromIssue("apply_result_log", "apply_ui_blueprint", Outcome(result.Success), result.Errors.FirstOrDefault())
        };

        var security = result.Errors.FirstOrDefault(error => IsSecurityRejection(error.Code));
        if (security is not null)
        {
            logs.Add(new(
                "security_rejection_log",
                "apply_ui_blueprint",
                "failed",
                security.Code,
                Redact(security.Message),
                Redact(security.RepairSuggestion),
                BlueprintPath: security.JsonPath));
        }

        if (!result.DryRun && result.Errors.Any(error => error.Code.Contains("Rollback", StringComparison.OrdinalIgnoreCase)))
        {
            logs.Add(LogFromIssue("rollback_log", "apply_ui_blueprint", "failed", result.Errors.FirstOrDefault()));
        }

        return Create(
            logs,
            DiagnosticCounts(result.Errors.Select(error => error.Code)),
            rollbackRate: logs.Any(log => log.EventName == "rollback_log") ? 1 : 0);
    }

    public static ComposerObservabilityPayload ForPreview(PreviewBlueprintResult result)
        => Create(
            [LogFromPreviewDiagnostic("compile_smoke_log", "preview_ui_blueprint", Outcome(result.BuildSucceeded), FirstPreviewDiagnostic(result.Diagnostics))],
            DiagnosticCounts(result.Diagnostics.Select(diagnostic => diagnostic.Code)),
            compileSmokeFailed: !result.BuildSucceeded);

    public static ComposerObservabilityPayload ForPackImport(
        string packId,
        string version,
        bool dryRun,
        int fileCount)
        => Create(
            [
                new(
                    "pack_import_log",
                    "pack_import",
                    "succeeded",
                    PackId: Redact(packId),
                    Message: dryRun ? "Pack import dry-run planned." : "Pack import completed.",
                    Remediation: "Review import plan before enabling writes.")
            ],
            [],
            packImportSuccessRate: 1,
            fileCount: fileCount,
            packageVersion: Redact(version));

    private static ComposerObservabilityPayload Create(
        IReadOnlyList<ComposerLogEntry> logs,
        IReadOnlyList<ComposerDiagnosticCount> diagnosticCounts,
        double packImportSuccessRate = 0,
        bool blueprintValidationFailed = false,
        bool rendererFailed = false,
        bool compileSmokeFailed = false,
        double rollbackRate = 0,
        int? fileCount = null,
        string? packageVersion = null)
        => new(
            Logs: logs,
            Metrics: new ComposerMetrics(
                PackImportSuccessRate: packImportSuccessRate,
                BlueprintValidationFailureRate: blueprintValidationFailed ? 1 : 0,
                RendererFailureRate: rendererFailed ? 1 : 0,
                CompileSmokeFailureRate: compileSmokeFailed ? 1 : 0,
                ApplyCancellationRate: 0,
                RollbackRate: rollbackRate,
                TopDiagnosticCodes: diagnosticCounts,
                FileCount: fileCount,
                PackageVersion: packageVersion),
            Privacy: new ComposerPrivacyPolicy(
                TelemetryEnabled: false,
                DisableTelemetrySetting: $"{McpServerConfiguration.ComposerTelemetryDisabledEnvVar}=true",
                AbsoluteLocalPathsIncluded: false,
                ExcludedFields: ExcludedFields));

    private static IReadOnlyList<ComposerDiagnosticCount> DiagnosticCounts(IEnumerable<string> codes)
        => codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .GroupBy(code => Redact(code), StringComparer.Ordinal)
            .Select(group => new ComposerDiagnosticCount(group.Key, group.Count()))
            .OrderByDescending(count => count.Count)
            .ThenBy(count => count.Code, StringComparer.Ordinal)
            .Take(10)
            .ToArray();

    private static string Outcome(bool success)
        => success ? "succeeded" : "failed";

    private static BlueprintValidationIssue? FirstIssue(BlueprintValidationResult result)
        => result.Errors.FirstOrDefault() ?? result.Warnings.FirstOrDefault();

    private static BlueprintValidationIssue? FirstIssue(RecipeExpansionResult result)
        => result.Errors.FirstOrDefault() ?? result.Warnings.FirstOrDefault();

    private static PreviewDiagnostic? FirstPreviewDiagnostic(IReadOnlyList<PreviewDiagnostic> diagnostics)
        => diagnostics.FirstOrDefault(diagnostic => diagnostic.Code != "PreviewXamlCompiled")
           ?? diagnostics.FirstOrDefault();

    private static ComposerLogEntry LogFromIssue(
        string eventName,
        string operation,
        string outcome,
        BlueprintValidationIssue? issue)
    {
        var blockKind = ExtractBlockKind(issue?.Message);
        return new(
            eventName,
            operation,
            outcome,
            issue?.Code,
            issue is null ? null : Redact(issue.Message),
            issue is null ? null : Redact(issue.RepairSuggestion),
            PackId: ExtractPackId(blockKind),
            BlockKind: blockKind,
            BlueprintPath: issue?.JsonPath);
    }

    private static ComposerLogEntry LogFromIssue(
        string eventName,
        string operation,
        string outcome,
        ApplyBlueprintIssue? issue)
        => new(
            eventName,
            operation,
            outcome,
            issue?.Code,
            issue is null ? null : Redact(issue.Message),
            issue is null ? null : Redact(issue.RepairSuggestion),
            BlueprintPath: issue?.JsonPath);

    private static ComposerLogEntry LogFromRepairAction(
        string eventName,
        string operation,
        string outcome,
        BlueprintRepairAction? action)
        => new(
            eventName,
            operation,
            outcome,
            action?.IssueCode,
            action is null ? null : Redact(action.Message),
            action is null ? null : Redact(action.SuggestedAction),
            BlueprintPath: action?.JsonPath,
            FilePath: action is null ? null : Redact(action.RendererTemplatePath ?? string.Empty));

    private static ComposerLogEntry LogFromPreviewDiagnostic(
        string eventName,
        string operation,
        string outcome,
        PreviewDiagnostic? diagnostic)
        => new(
            eventName,
            operation,
            outcome,
            diagnostic?.Code,
            diagnostic is null ? null : Redact(diagnostic.Message),
            diagnostic is null ? null : "Fix the mapped blueprint node or renderer template, then rerun preview.",
            BlueprintPath: diagnostic?.JsonPath,
            FilePath: diagnostic is null ? null : Redact(diagnostic.RendererTemplatePath));

    private static string? ExtractBlockKind(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = BlockKindPattern().Match(message);
        return match.Success ? Redact(match.Groups["kind"].Value) : null;
    }

    private static string? ExtractPackId(string? blockKind)
    {
        var separator = blockKind?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        return separator > 0 ? blockKind![..separator] : null;
    }

    private static bool IsSecurityRejection(string code)
        => code is "ProjectWritesDisabled" or "ProjectRootNotAllowlisted" or "InvalidProjectRootAllowlist"
            || code.Contains("Policy", StringComparison.OrdinalIgnoreCase);

    private static string Redact(string value)
        => ComposerPathRedactor.Redact(value);

    [GeneratedRegex(@"Block kind '(?<kind>[^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex BlockKindPattern();
}

internal sealed record ComposerObservabilityPayload(
    IReadOnlyList<ComposerLogEntry> Logs,
    ComposerMetrics Metrics,
    ComposerPrivacyPolicy Privacy);

internal sealed record ComposerLogEntry(
    string EventName,
    string Operation,
    string Outcome,
    string? Code = null,
    string? Message = null,
    string? Remediation = null,
    string? PackId = null,
    string? BlockKind = null,
    string? BlueprintPath = null,
    string? FilePath = null);

internal sealed record ComposerMetrics(
    double PackImportSuccessRate,
    double BlueprintValidationFailureRate,
    double RendererFailureRate,
    double CompileSmokeFailureRate,
    double ApplyCancellationRate,
    double RollbackRate,
    IReadOnlyList<ComposerDiagnosticCount> TopDiagnosticCodes,
    int? FileCount = null,
    string? PackageVersion = null);

internal sealed record ComposerDiagnosticCount(string Code, int Count);

internal sealed record ComposerPrivacyPolicy(
    bool TelemetryEnabled,
    string DisableTelemetrySetting,
    bool AbsoluteLocalPathsIncluded,
    IReadOnlyList<string> ExcludedFields);
