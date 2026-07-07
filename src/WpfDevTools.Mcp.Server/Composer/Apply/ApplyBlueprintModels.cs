using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed record ApplyBlueprintRequest(
    string BlueprintJson,
    string ProjectRoot,
    string? TargetPath = null,
    bool DryRun = true,
    bool ConfirmApply = false);

internal sealed record ApplyBlueprintResult(
    bool Success,
    bool Valid,
    bool DryRun,
    bool RequiresConfirmation,
    bool WouldWriteFiles,
    string Xaml,
    IReadOnlyList<ApplyFilePlanItem> FilePlan,
    IReadOnlyList<string> ResourcePlan,
    IReadOnlyList<RequiredNuGetPackage> RequiredNuGetPackages,
    ViewModelBindingContractPlan ViewModelBindingContract,
    IReadOnlyList<ApplyBlueprintIssue> Errors)
{
    public static ApplyBlueprintResult CreateValid(
        bool dryRun,
        bool requiresConfirmation,
        bool wouldWriteFiles,
        string xaml,
        IReadOnlyList<ApplyFilePlanItem> filePlan,
        IReadOnlyList<string> resourcePlan,
        IReadOnlyList<RequiredNuGetPackage> packages,
        ViewModelBindingContractPlan viewModelBindingContract,
        IReadOnlyList<ApplyBlueprintIssue> errors)
        => new(true, true, dryRun, requiresConfirmation, wouldWriteFiles, xaml, filePlan, resourcePlan, packages, viewModelBindingContract, errors);

    public static ApplyBlueprintResult Invalid(
        bool dryRun,
        IReadOnlyList<ApplyBlueprintIssue> errors,
        bool requiresConfirmation = false)
        => new(false, false, dryRun, requiresConfirmation, false, string.Empty, [], [], [], new ViewModelBindingContractPlan(string.Empty, string.Empty, false), errors);
}

internal sealed record ApplyFilePlanItem(
    string Role,
    string TargetPath,
    string Action,
    bool WouldWrite,
    string RiskLevel,
    string? BackupPath,
    bool Reversible);

internal sealed record ViewModelBindingContractPlan(string TargetPath, string Content, bool WouldWrite);

internal sealed record ApplyBlueprintIssue(string JsonPath, string Code, string Message, string RepairSuggestion)
{
    public static ApplyBlueprintIssue FromValidationIssue(Composer.Blueprints.BlueprintValidationIssue issue)
        => new(issue.JsonPath, issue.Code, issue.Message, issue.RepairSuggestion);
}

internal sealed record ApplyFileWriteResult(bool Success, string? BackupPath, ApplyBlueprintIssue? Error)
{
    public static ApplyFileWriteResult CreateSuccess(string? backupPath)
        => new(true, backupPath, null);

    public static ApplyFileWriteResult CreateFailure(string? backupPath, ApplyBlueprintIssue error)
        => new(false, backupPath, error);
}

internal sealed record ExistingContentReadResult(bool Success, string? Content, ApplyBlueprintIssue? Error)
{
    public static ExistingContentReadResult CreateSuccess(string? content)
        => new(true, content, null);

    public static ExistingContentReadResult CreateFailure(ApplyBlueprintIssue error)
        => new(false, null, error);
}
