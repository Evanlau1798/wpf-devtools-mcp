using System.Text.Json;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class RestoreStateSnapshotTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var snapshotId = ParseStringParam(arguments, "snapshotId");
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            return CreateMissingParamError("snapshotId");
        }

        var removeAfterRestore = ParseBoolParam(arguments, "removeAfterRestore") ?? true;

        if (!_sessionManager.TryGetStateSnapshot(processId, snapshotId, out var snapshot) || snapshot == null)
        {
            return new ToolErrorPayload
            {
                Error = $"No stored snapshot found for snapshotId '{snapshotId}'.",
                ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
                Hint = "Call capture_state_snapshot first or verify the snapshotId before retrying restore_state_snapshot."
            };
        }

        var warnings = new List<string>();
        var (restoredDependencyProperties, skippedDependencyProperties) = await RestoreDependencyPropertiesAsync(
            processId,
            snapshot.DependencyProperties,
            warnings,
            cancellationToken).ConfigureAwait(false);
        var (restoredViewModelProperties, skippedViewModelProperties) = await RestoreViewModelPropertiesAsync(
            processId,
            snapshot.ViewModelProperties,
            warnings,
            cancellationToken).ConfigureAwait(false);
        var restoredFocus = await RestoreFocusAsync(
            processId,
            snapshot.Focus,
            warnings,
            cancellationToken).ConfigureAwait(false);

        if (removeAfterRestore && warnings.Count == 0)
        {
            _sessionManager.RemoveStateSnapshot(processId, snapshotId);
        }

        if (warnings.Count == 0
            && _sessionManager.TryGetNavigationState(processId, out var navigationState)
            && string.Equals(navigationState?.ActiveSnapshotId, snapshotId, StringComparison.Ordinal))
        {
            _sessionManager.ClearActiveSnapshotId(processId);
        }

        return new
        {
            success = warnings.Count == 0,
            restoredDependencyPropertyCount = restoredDependencyProperties,
            skippedDependencyPropertyCount = skippedDependencyProperties.Count,
            skippedDependencyProperties,
            restoredViewModelPropertyCount = restoredViewModelProperties,
            skippedViewModelPropertyCount = skippedViewModelProperties.Count,
            skippedViewModelProperties,
            restoredFocus,
            warnings
        };
    }

    private async Task<(int restoredCount, List<object> skippedProperties)> RestoreDependencyPropertiesAsync(
        int processId,
        IReadOnlyList<StoredDependencyPropertySnapshot> snapshots,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var restored = 0;
        var skipped = new List<object>();
        foreach (var snapshot in snapshots)
        {
            if (snapshot.IsExpression && !string.IsNullOrWhiteSpace(snapshot.ExpressionRestoreToken))
            {
                var restoreResponse = JsonSerializer.SerializeToElement(await SendInspectorRequestWithoutPiggybackAsync(
                    processId,
                    "restore_dp_expression",
                    new
                    {
                        elementId = snapshot.ElementId,
                        propertyName = snapshot.PropertyName,
                        restoreToken = snapshot.ExpressionRestoreToken
                    },
                    cancellationToken).ConfigureAwait(false));

                if (IsSuccess(restoreResponse))
                {
                    restored++;
                    continue;
                }

                warnings.Add($"DependencyProperty restore failed for '{snapshot.PropertyName}'.");
                continue;
            }

            if (!snapshot.CanRestore)
            {
                var verification = await VerifySkippedDependencyPropertyAsync(
                    processId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                skipped.Add(new
                {
                    propertyName = snapshot.PropertyName,
                    reason = snapshot.SkipReason ?? $"Property '{snapshot.PropertyName}' cannot be deterministically restored.",
                    restoreDisposition = ClassifyDependencyPropertyRestoreDisposition(snapshot),
                    verified = verification.verified,
                    expectedValue = snapshot.CurrentValue,
                    currentValue = verification.currentValue
                });

                if (!verification.verified)
                {
                    warnings.Add($"DependencyProperty restore verification failed for '{snapshot.PropertyName}'.");
                }

                continue;
            }

            object parameters = snapshot.HadLocalValue
                ? new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName, value = snapshot.LocalValue }
                : new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName };
            var method = snapshot.HadLocalValue ? "set_dp_value" : "clear_dp_value";
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestWithoutPiggybackAsync(
                processId,
                method,
                parameters,
                cancellationToken).ConfigureAwait(false));

            if (IsSuccess(response))
            {
                restored++;
                continue;
            }

            warnings.Add($"DependencyProperty restore failed for '{snapshot.PropertyName}'.");
        }

        return (restored, skipped);
    }

    private async Task<(int restoredCount, List<object> skippedProperties)> RestoreViewModelPropertiesAsync(
        int processId,
        IReadOnlyList<StoredViewModelPropertySnapshot> snapshots,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var restored = 0;
        var skipped = new List<object>();
        foreach (var snapshot in snapshots)
        {
            if (!snapshot.CanRestore)
            {
                var verification = await VerifySkippedViewModelPropertyAsync(
                    processId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                skipped.Add(new
                {
                    propertyName = snapshot.PropertyName,
                    reason = snapshot.SkipReason ?? $"Property '{snapshot.PropertyName}' is not writable.",
                    restoreDisposition = ClassifyRestoreDisposition(snapshot),
                    verified = verification.verified,
                    expectedValue = snapshot.Value,
                    currentValue = verification.currentValue
                });

                if (!verification.verified)
                {
                    warnings.Add($"ViewModel restore verification failed for skipped property '{snapshot.PropertyName}'.");
                }

                continue;
            }

            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestWithoutPiggybackAsync(
                processId,
                "modify_viewmodel",
                new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName, value = snapshot.Value },
                cancellationToken).ConfigureAwait(false));

            if (IsSuccess(response))
            {
                restored++;
                continue;
            }

            warnings.Add($"ViewModel restore failed for '{snapshot.PropertyName}'.");
        }

        return (restored, skipped);
    }

    private async Task<(bool verified, string? currentValue)> VerifySkippedViewModelPropertyAsync(
        int processId,
        StoredViewModelPropertySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestWithoutPiggybackAsync(
            processId,
            "get_viewmodel",
            new { elementId = snapshot.ElementId },
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            return (false, null);
        }

        var property = response.GetProperty("properties")
            .EnumerateArray()
            .FirstOrDefault(item => string.Equals(
                GetOptionalString(item, "name"),
                snapshot.PropertyName,
                StringComparison.Ordinal));

        if (property.ValueKind == JsonValueKind.Undefined)
        {
            return (false, null);
        }

        var currentValue = GetOptionalString(property, "value");
        return (string.Equals(currentValue, snapshot.Value, StringComparison.Ordinal), currentValue);
    }

    private async Task<bool> RestoreFocusAsync(
        int processId,
        StoredFocusSnapshot? snapshot,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (snapshot?.FocusedElementId == null)
        {
            return false;
        }

        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestWithoutPiggybackAsync(
            processId,
            "focus_element",
            new { elementId = snapshot.FocusedElementId },
            cancellationToken).ConfigureAwait(false));

        if (IsSuccess(response))
        {
            return true;
        }

        warnings.Add("Focus restore failed.");
        return false;
    }

    private async Task<(bool verified, string? currentValue)> VerifySkippedDependencyPropertyAsync(
        int processId,
        StoredDependencyPropertySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestWithoutPiggybackAsync(
            processId,
            "get_dp_value_source",
            new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName },
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            return (false, null);
        }

        var currentValue = GetOptionalString(response, "currentValue");
        var currentIsExpression = response.TryGetProperty("isExpression", out var isExpressionProperty) &&
            isExpressionProperty.ValueKind == JsonValueKind.True;
        var verified = string.Equals(currentValue, snapshot.CurrentValue, StringComparison.Ordinal) &&
            currentIsExpression == snapshot.IsExpression;
        return (verified, currentValue);
    }

    private static bool IsSuccess(JsonElement response) =>
        response.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static string ClassifyRestoreDisposition(StoredViewModelPropertySnapshot snapshot)
    {
        if (snapshot.SkipReason?.IndexOf("complex reference", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "SkippedComplexReference";
        }

        return "SkippedReadOnly";
    }

    private static string ClassifyDependencyPropertyRestoreDisposition(StoredDependencyPropertySnapshot snapshot)
    {
        if (snapshot.IsExpression)
        {
            return "SkippedExpression";
        }

        return "SkippedUnsupported";
    }
}
