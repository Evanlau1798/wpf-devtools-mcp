using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.Composer.Drafts;

public sealed record BlueprintDraftPathOperation(
    [property: Required]
    [property: StringLength(BoundaryStringLimits.MaxStringArgumentLength)]
    [property: Description("Exact JSON path or stable @Element alias to set or remove.")]
    string JsonPath,
    [property: Description("JSON value for set mode. Omit only when remove=true.")]
    JsonElement? Value = null,
    [property: Description("When true, remove the exact target and omit value.")]
    bool Remove = false)
{
    public const int MaxOperations = 16;
}

internal sealed record BlueprintDraftIssue(
    string Code,
    string Message,
    string RepairSuggestion,
    string? RequestJsonPath = null);

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
