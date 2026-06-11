using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Mcp.Server.Diagnostics;

internal static class SceneStateDiffCalculator
{
    public static SceneStateDiffResult Calculate(
        StoredStateSnapshot snapshot,
        CurrentSceneState currentState,
        string? trigger,
        DateTimeOffset nowUtc)
    {
        var propertyChanges = CalculatePropertyChanges(snapshot, currentState);
        var viewModelChanges = CalculateViewModelChanges(snapshot, currentState);
        var newBindingErrors = snapshot.HasBindingErrorBaseline
            ? CalculateAdded(snapshot.BindingErrors, currentState.BindingErrors, BindingKey)
            : [];
        var resolvedBindingErrors = snapshot.HasBindingErrorBaseline
            ? CalculateRemoved(snapshot.BindingErrors, currentState.BindingErrors, BindingKey)
            : [];
        var validationChanges = snapshot.HasValidationBaseline
            ? CalculateValidationChanges(snapshot.ValidationErrors, currentState.ValidationErrors)
            : [];

        var focusChange = CalculateFocusChange(snapshot.Focus, currentState.Focus);

        return new SceneStateDiffResult(
            true,
            snapshot.SnapshotId,
            trigger,
            Math.Max(0, (long)(nowUtc - snapshot.CapturedAtUtc).TotalMilliseconds),
            propertyChanges,
            viewModelChanges,
            newBindingErrors,
            resolvedBindingErrors,
            validationChanges,
            focusChange);
    }

    private static IReadOnlyList<PropertyChange> CalculatePropertyChanges(StoredStateSnapshot snapshot, CurrentSceneState currentState)
    {
        var currentMap = currentState.DependencyProperties.ToDictionary(
            item => (item.ElementId, item.PropertyName),
            item => item);

        return snapshot.DependencyProperties
            .Where(item => currentMap.TryGetValue((item.ElementId, item.PropertyName), out var current) &&
                (!string.Equals(item.CurrentValue, current.CurrentValue, StringComparison.Ordinal) ||
                 !string.Equals(item.BaseValueSource, current.BaseValueSource, StringComparison.Ordinal)))
            .Select(item =>
            {
                var current = currentMap[(item.ElementId, item.PropertyName)];
                return new PropertyChange(
                    item.ElementId,
                    item.PropertyName,
                    item.CurrentValue,
                    current.CurrentValue,
                    item.BaseValueSource,
                    current.BaseValueSource);
            })
            .ToArray();
    }

    private static IReadOnlyList<ViewModelChange> CalculateViewModelChanges(StoredStateSnapshot snapshot, CurrentSceneState currentState)
    {
        var currentMap = currentState.ViewModelProperties.ToDictionary(
            item => (item.ElementId, item.PropertyName),
            item => item);

        return snapshot.ViewModelProperties
            .Where(item => currentMap.TryGetValue((item.ElementId, item.PropertyName), out var current) &&
                !string.Equals(item.Value, current.Value, StringComparison.Ordinal))
            .Select(item =>
            {
                var current = currentMap[(item.ElementId, item.PropertyName)];
                return new ViewModelChange(item.ElementId, item.PropertyName, item.Value, current.Value);
            })
            .ToArray();
    }

    private static IReadOnlyList<StoredBindingErrorSnapshot> CalculateAdded(
        IReadOnlyList<StoredBindingErrorSnapshot> before,
        IReadOnlyList<StoredBindingErrorSnapshot> after,
        Func<StoredBindingErrorSnapshot, string> keySelector)
    {
        var beforeKeys = before.Select(keySelector).ToHashSet(StringComparer.Ordinal);
        return after.Where(item => !beforeKeys.Contains(keySelector(item))).ToArray();
    }

    private static IReadOnlyList<StoredBindingErrorSnapshot> CalculateRemoved(
        IReadOnlyList<StoredBindingErrorSnapshot> before,
        IReadOnlyList<StoredBindingErrorSnapshot> after,
        Func<StoredBindingErrorSnapshot, string> keySelector)
    {
        var afterKeys = after.Select(keySelector).ToHashSet(StringComparer.Ordinal);
        return before.Where(item => !afterKeys.Contains(keySelector(item))).ToArray();
    }

    private static IReadOnlyList<ValidationChange> CalculateValidationChanges(
        IReadOnlyList<StoredValidationErrorSnapshot> before,
        IReadOnlyList<StoredValidationErrorSnapshot> after)
    {
        var afterKeys = after.ToDictionary(ValidationKey, item => item, StringComparer.Ordinal);
        var beforeKeys = before.ToDictionary(ValidationKey, item => item, StringComparer.Ordinal);
        var changes = new List<ValidationChange>();

        foreach (var item in before)
        {
            if (!afterKeys.ContainsKey(ValidationKey(item)))
            {
                changes.Add(new ValidationChange("Removed", item.ElementType, item.ElementName, item.ErrorContent, item.IsRuleError, item.RuleType));
            }
        }

        foreach (var item in after)
        {
            if (!beforeKeys.ContainsKey(ValidationKey(item)))
            {
                changes.Add(new ValidationChange("Added", item.ElementType, item.ElementName, item.ErrorContent, item.IsRuleError, item.RuleType));
            }
        }

        return changes;
    }

    private static FocusChange? CalculateFocusChange(StoredFocusSnapshot? before, CurrentFocusState? after)
    {
        if (before == null && after == null)
        {
            return null;
        }

        var changed = !string.Equals(before?.FocusedElementId, after?.FocusedElementId, StringComparison.Ordinal) ||
            !string.Equals(before?.FocusKind, after?.FocusKind, StringComparison.Ordinal);

        return new FocusChange(
            changed,
            before?.FocusKind,
            before?.FocusedElementId,
            after?.FocusKind,
            after?.FocusedElementId);
    }

    private static string BindingKey(StoredBindingErrorSnapshot item) =>
        string.Join("|", item.ElementId, item.SuggestedElementId, item.PropertyName, item.BindingPath, item.Message);

    private static string ValidationKey(StoredValidationErrorSnapshot item) =>
        string.Join("|", item.ElementType, item.ElementName, item.ErrorContent, item.IsRuleError, item.RuleType);
}

internal sealed record CurrentSceneState(
    IReadOnlyList<CurrentDependencyPropertyState> DependencyProperties,
    IReadOnlyList<CurrentViewModelPropertyState> ViewModelProperties,
    CurrentFocusState? Focus,
    IReadOnlyList<StoredBindingErrorSnapshot> BindingErrors,
    IReadOnlyList<StoredValidationErrorSnapshot> ValidationErrors);

internal sealed record CurrentDependencyPropertyState(
    string? ElementId,
    string PropertyName,
    string? CurrentValue,
    string? BaseValueSource);

internal sealed record CurrentViewModelPropertyState(
    string? ElementId,
    string PropertyName,
    string? Value);

internal sealed record CurrentFocusState(
    string? FocusKind,
    string? FocusedElementId);

internal sealed record SceneStateDiffResult(
    bool Success,
    string SnapshotId,
    string? Trigger,
    long DurationMs,
    IReadOnlyList<PropertyChange> PropertyChanges,
    IReadOnlyList<ViewModelChange> ViewModelChanges,
    IReadOnlyList<StoredBindingErrorSnapshot> NewBindingErrors,
    IReadOnlyList<StoredBindingErrorSnapshot> ResolvedBindingErrors,
    IReadOnlyList<ValidationChange> ValidationChanges,
    FocusChange? FocusChange);

internal sealed record PropertyChange(
    string? ElementId,
    string PropertyName,
    string? BeforeValue,
    string? AfterValue,
    string? BeforeBaseValueSource,
    string? AfterBaseValueSource);

internal sealed record ViewModelChange(
    string? ElementId,
    string PropertyName,
    string? BeforeValue,
    string? AfterValue);

internal sealed record ValidationChange(
    string ChangeType,
    string ElementType,
    string? ElementName,
    string ErrorContent,
    bool IsRuleError,
    string? RuleType);

internal sealed record FocusChange(
    bool Changed,
    string? BeforeFocusKind,
    string? BeforeFocusedElementId,
    string? AfterFocusKind,
    string? AfterFocusedElementId);
