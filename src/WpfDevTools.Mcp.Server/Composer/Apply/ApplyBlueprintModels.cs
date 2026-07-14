using System.Text.Json;
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
    BehaviorIntegrationContractPlan BehaviorIntegrationContract,
    ProjectIntegrationPlan ProjectIntegrationPlan,
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
        BehaviorIntegrationContractPlan behaviorIntegrationContract,
        ProjectIntegrationPlan projectIntegrationPlan,
        IReadOnlyList<ApplyBlueprintIssue> errors)
        => new(true, true, dryRun, requiresConfirmation, wouldWriteFiles, xaml, filePlan, resourcePlan, packages, viewModelBindingContract, behaviorIntegrationContract, projectIntegrationPlan, errors);

    public static ApplyBlueprintResult Invalid(
        bool dryRun,
        IReadOnlyList<ApplyBlueprintIssue> errors,
        bool requiresConfirmation = false)
        => new(false, false, dryRun, requiresConfirmation, false, string.Empty, [], [], [], new ViewModelBindingContractPlan(string.Empty, string.Empty, false, null), BehaviorIntegrationContractPlan.Empty, ProjectIntegrationPlan.Empty, errors);
}

internal sealed record ApplyFilePlanItem(
    string Role,
    string TargetPath,
    string Action,
    bool WouldWrite,
    string RiskLevel,
    string? BackupPath,
    bool Reversible);

internal sealed record ViewModelBindingContractPlan(
    string TargetPath,
    string Content,
    bool WouldWrite,
    JsonElement? BindingRequirements);

internal sealed record BehaviorIntegrationContractPlan(
    string Status,
    string? SourceRecipeId,
    IReadOnlyList<BehaviorInteractionPlan> Interactions,
    string ImplementationGuidance,
    string VerificationGuidance)
{
    public static readonly BehaviorIntegrationContractPlan Empty = new(
        "not-available",
        null,
        [],
        "Behavior integration is unavailable because the blueprint was invalid.",
        "Correct the blueprint errors before validating application behavior.");
}

internal sealed record BehaviorInteractionPlan(
    string Kind,
    string BindingStatus,
    string CommandBinding,
    string? CommandPath,
    string? CommandParameter,
    string? TargetPageTag,
    string? Label,
    string ImplementationGuidance);

internal sealed record ApplyBlueprintIssue(string JsonPath, string Code, string Message, string RepairSuggestion)
{
    public static ApplyBlueprintIssue FromValidationIssue(Composer.Blueprints.BlueprintValidationIssue issue)
        => new(issue.JsonPath, issue.Code, issue.Message, issue.RepairSuggestion);
}

internal sealed record ApplyFileWriteResult(bool Success, string? BackupPath, bool TargetExisted, ApplyBlueprintIssue? Error)
{
    public static ApplyFileWriteResult CreateSuccess(string? backupPath, bool targetExisted)
        => new(true, backupPath, targetExisted, null);

    public static ApplyFileWriteResult CreateFailure(string? backupPath, bool targetExisted, ApplyBlueprintIssue error)
        => new(false, backupPath, targetExisted, error);
}

internal sealed record ExistingContentReadResult(bool Success, string? Content, ApplyBlueprintIssue? Error)
{
    public static ExistingContentReadResult CreateSuccess(string? content)
        => new(true, content, null);

    public static ExistingContentReadResult CreateFailure(ApplyBlueprintIssue error)
        => new(false, null, error);
}
