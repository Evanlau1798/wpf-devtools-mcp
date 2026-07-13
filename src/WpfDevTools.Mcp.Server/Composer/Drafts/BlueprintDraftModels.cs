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
    BlueprintDraftIssue? Error)
{
    public static BlueprintDraftMutationResult Invalid(BlueprintDraftIssue error)
        => new(false, string.Empty, 0, default, error);
}

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
