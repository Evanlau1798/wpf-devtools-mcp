using System.Text.Json.Serialization;

namespace WpfDevTools.Mcp.Server.Composer.Drafts;

internal sealed record BlueprintDraftIssue(
    string Code,
    string Message,
    string RepairSuggestion);

internal sealed record BlueprintDraftMutationResult(
    bool Success,
    string DraftRef,
    int CharacterCount,
    DateTimeOffset ExpiresAt,
    BlueprintDraftIssue? Error,
    BlueprintDraftChangeSummary? ChangeSummary = null)
{
    public static BlueprintDraftMutationResult Invalid(BlueprintDraftIssue error)
        => new(false, string.Empty, 0, default, error);
}

internal sealed record BlueprintDraftChangeSummary(
    int ChangeCount,
    int ReportedChangeCount,
    bool Truncated,
    IReadOnlyList<BlueprintDraftChange> Changes);

internal sealed record BlueprintDraftChange(
    string JsonPath,
    string ChangeType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    string? Before,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    string? After);

internal sealed record BlueprintDraftResolution(
    bool Success,
    string DraftRef,
    string BlueprintJson,
    int CharacterCount,
    DateTimeOffset ExpiresAt,
    BlueprintDraftIssue? Error)
{
    public static BlueprintDraftResolution Invalid(string draftRef, BlueprintDraftIssue error)
        => new(false, draftRef, string.Empty, 0, default, error);
}

internal sealed record BlueprintInputResolution(
    bool Success,
    bool IsDraft,
    string DraftRef,
    string BlueprintJson,
    BlueprintDraftIssue? Error);
