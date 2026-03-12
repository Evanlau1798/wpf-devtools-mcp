namespace WpfDevTools.Mcp.Server.State;

internal sealed record StoredStateSnapshot(
    string SnapshotId,
    string? SnapshotName,
    string? ElementId,
    IReadOnlyList<StoredDependencyPropertySnapshot> DependencyProperties,
    IReadOnlyList<StoredViewModelPropertySnapshot> ViewModelProperties,
    StoredFocusSnapshot? Focus,
    IReadOnlyList<StoredBindingErrorSnapshot> BindingErrors,
    bool HasBindingErrorBaseline,
    IReadOnlyList<StoredValidationErrorSnapshot> ValidationErrors,
    bool HasValidationBaseline,
    DateTimeOffset CapturedAtUtc);

internal sealed record StoredDependencyPropertySnapshot(
    string? ElementId,
    string PropertyName,
    bool HadLocalValue,
    string? LocalValue,
    string? CurrentValue,
    string? BaseValueSource);

internal sealed record StoredViewModelPropertySnapshot(
    string? ElementId,
    string PropertyName,
    string? PropertyType,
    string? Value,
    bool CanRestore,
    string? SkipReason);

internal sealed record StoredFocusSnapshot(
    string? FocusKind,
    string? FocusedElementId);

internal sealed record StoredBindingErrorSnapshot(
    string? ElementId,
    string? SuggestedElementId,
    string? MatchConfidence,
    string? PropertyName,
    string? BindingPath,
    string? Message);

internal sealed record StoredValidationErrorSnapshot(
    string ElementType,
    string? ElementName,
    string ErrorContent,
    bool IsRuleError,
    string? RuleType);
