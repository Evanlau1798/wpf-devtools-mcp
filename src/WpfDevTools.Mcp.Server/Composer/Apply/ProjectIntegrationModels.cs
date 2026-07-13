using System.Text.Json.Serialization;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed record ProjectIntegrationRequest(
    string BlueprintJson,
    string ProjectRoot,
    string? TargetPath,
    string ReviewedPlanHash,
    bool ConfirmIntegration = false);

internal sealed record ProjectIntegrationPlan(
    bool Ready,
    string PlanHash,
    IReadOnlyList<ProjectIntegrationOperation> Operations,
    IReadOnlyList<ApplyBlueprintIssue> Errors)
{
    public static readonly ProjectIntegrationPlan Empty = new(false, string.Empty, [], []);
}

internal sealed record ProjectIntegrationOperation(
    string Role,
    string TargetPath,
    string Action,
    IReadOnlyList<string> Purposes,
    ProjectFilePrecondition Precondition,
    string ProposedSha256,
    string Description,
    [property: JsonIgnore] string ProposedContent);

internal sealed record ProjectFilePrecondition(bool Exists, string Sha256);

internal sealed record ProjectIntegrationResult(
    bool Success,
    bool Applied,
    bool RolledBack,
    string PlanHash,
    IReadOnlyList<ProjectIntegrationChange> Changes,
    IReadOnlyList<ApplyBlueprintIssue> Errors)
{
    public static ProjectIntegrationResult Invalid(
        string planHash,
        IReadOnlyList<ApplyBlueprintIssue> errors,
        bool rolledBack = false,
        IReadOnlyList<ProjectIntegrationChange>? changes = null)
        => new(false, false, rolledBack, planHash, changes ?? [], errors);
}

internal sealed record ProjectIntegrationChange(
    string Role,
    string TargetPath,
    string Action,
    string? BackupPath,
    string RollbackAction);
