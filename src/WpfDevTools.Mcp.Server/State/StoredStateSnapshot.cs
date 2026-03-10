namespace WpfDevTools.Mcp.Server.State;

internal sealed record StoredStateSnapshot(
    string SnapshotId,
    string? SnapshotName,
    string? ElementId,
    IReadOnlyList<StoredDependencyPropertySnapshot> DependencyProperties,
    IReadOnlyList<StoredViewModelPropertySnapshot> ViewModelProperties,
    StoredFocusSnapshot? Focus,
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
    string? Value);

internal sealed record StoredFocusSnapshot(
    string? FocusKind,
    string? FocusedElementId);
