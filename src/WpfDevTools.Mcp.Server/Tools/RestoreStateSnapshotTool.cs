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

        var progress = new RestoreProgress();
        try
        {
            await RestoreDependencyPropertiesAsync(
                processId,
                snapshot.DependencyProperties,
                progress,
                cancellationToken).ConfigureAwait(false);
            await RestoreViewModelPropertiesAsync(
                processId,
                snapshot.ViewModelProperties,
                progress,
                cancellationToken).ConfigureAwait(false);
            progress.RestoredFocus = await RestoreFocusAsync(
                processId,
                snapshot.Focus,
                progress.Warnings,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRestoreInterrupted(ex))
        {
            progress.Warnings.Add("Restore was interrupted before all snapshot state could be verified; reconnect and re-read state before retrying.");
            return CreateInterruptedRestoreResult(processId, snapshotId, progress);
        }

        if (removeAfterRestore && progress.Warnings.Count == 0)
        {
            _sessionManager.RemoveStateSnapshot(processId, snapshotId);
        }

        if (progress.Warnings.Count == 0
            && _sessionManager.TryGetNavigationState(processId, out var navigationState)
            && string.Equals(navigationState?.ActiveSnapshotId, snapshotId, StringComparison.Ordinal))
        {
            _sessionManager.ClearActiveSnapshotId(processId);
        }

        return CreateRestoreResult(progress);
    }

    private async Task RestoreDependencyPropertiesAsync(
        int processId,
        IReadOnlyList<StoredDependencyPropertySnapshot> snapshots,
        RestoreProgress progress,
        CancellationToken cancellationToken)
    {
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
                    var verification = await VerifyDependencyPropertyAsync(
                        processId,
                        snapshot,
                        cancellationToken).ConfigureAwait(false);
                    progress.RestoredDependencyProperties.Add(CreateDependencyPropertyVerificationResult(snapshot, verification));
                    if (!verification.verified)
                    {
                        progress.Warnings.Add($"DependencyProperty restore verification failed for '{snapshot.PropertyName}'.");
                    }

                    progress.RestoredDependencyPropertyCount++;
                    continue;
                }

                progress.Warnings.Add($"DependencyProperty restore failed for '{snapshot.PropertyName}'.");
                continue;
            }

            if (!snapshot.CanRestore)
            {
                var verification = await VerifyDependencyPropertyAsync(
                    processId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                progress.SkippedDependencyProperties.Add(new
                {
                    propertyName = snapshot.PropertyName,
                    reason = snapshot.SkipReason ?? $"Property '{snapshot.PropertyName}' cannot be deterministically restored.",
                    restoreDisposition = ClassifyDependencyPropertyRestoreDisposition(snapshot),
                    verified = verification.verified,
                    expectedValue = snapshot.CurrentValue,
                    currentValue = verification.currentValue,
                    verificationSkippedReason = verification.skippedReason
                });

                if (!verification.verified)
                {
                    progress.Warnings.Add($"DependencyProperty restore verification failed for '{snapshot.PropertyName}'.");
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
                var verification = await VerifyDependencyPropertyAsync(
                    processId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                progress.RestoredDependencyProperties.Add(CreateDependencyPropertyVerificationResult(snapshot, verification));
                if (!verification.verified)
                {
                    progress.Warnings.Add($"DependencyProperty restore verification failed for '{snapshot.PropertyName}'.");
                }

                progress.RestoredDependencyPropertyCount++;
                continue;
            }

            progress.Warnings.Add($"DependencyProperty restore failed for '{snapshot.PropertyName}'.");
        }
    }

    private async Task RestoreViewModelPropertiesAsync(
        int processId,
        IReadOnlyList<StoredViewModelPropertySnapshot> snapshots,
        RestoreProgress progress,
        CancellationToken cancellationToken)
    {
        foreach (var snapshot in snapshots)
        {
            if (!snapshot.CanRestore)
            {
                var verification = await VerifyViewModelPropertyAsync(
                    processId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                progress.SkippedViewModelProperties.Add(new
                {
                    propertyName = snapshot.PropertyName,
                    reason = snapshot.SkipReason ?? $"Property '{snapshot.PropertyName}' is not writable.",
                    restoreDisposition = ClassifyRestoreDisposition(snapshot),
                    verified = verification.verified,
                    expectedValue = snapshot.Value,
                    currentValue = verification.currentValue,
                    verificationSkippedReason = verification.skippedReason
                });

                if (!verification.verified)
                {
                    progress.Warnings.Add($"ViewModel restore verification failed for skipped property '{snapshot.PropertyName}'.");
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
                var verification = await VerifyViewModelPropertyAsync(
                    processId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                progress.RestoredViewModelProperties.Add(new
                {
                    propertyName = snapshot.PropertyName,
                    verified = verification.verified,
                    expectedValue = snapshot.Value,
                    currentValue = verification.currentValue,
                    verificationSkippedReason = verification.skippedReason
                });

                if (!verification.verified)
                {
                    progress.Warnings.Add($"ViewModel restore verification failed for '{snapshot.PropertyName}'.");
                }

                progress.RestoredViewModelPropertyCount++;
                continue;
            }

            progress.Warnings.Add($"ViewModel restore failed for '{snapshot.PropertyName}'.");
        }
    }

    private async Task<(bool verified, string? currentValue, string? skippedReason)> VerifyViewModelPropertyAsync(
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
            return (false, null, "get_viewmodel read-back failed.");
        }

        var property = response.GetProperty("properties")
            .EnumerateArray()
            .FirstOrDefault(item => string.Equals(
                GetOptionalString(item, "name"),
                snapshot.PropertyName,
                StringComparison.Ordinal));

        if (property.ValueKind == JsonValueKind.Undefined)
        {
            return (false, null, $"ViewModel property '{snapshot.PropertyName}' was not returned by get_viewmodel.");
        }

        var currentValue = GetOptionalString(property, "value");
        return (string.Equals(currentValue, snapshot.Value, StringComparison.Ordinal), currentValue, null);
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

    private async Task<(bool verified, string? currentValue, bool? currentIsExpression, string? currentBaseValueSource, string? skippedReason)> VerifyDependencyPropertyAsync(
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
            return (false, null, null, null, "get_dp_value_source read-back failed.");
        }

        var currentValue = GetOptionalString(response, "currentValue");
        var currentIsExpression = response.TryGetProperty("isExpression", out var isExpressionProperty) &&
            isExpressionProperty.ValueKind == JsonValueKind.True;
        var currentBaseValueSource = GetOptionalString(response, "baseValueSource");
        var baseValueSourceMatches = string.IsNullOrWhiteSpace(snapshot.BaseValueSource) ||
            string.Equals(currentBaseValueSource, snapshot.BaseValueSource, StringComparison.Ordinal);
        var verified = string.Equals(currentValue, snapshot.CurrentValue, StringComparison.Ordinal) &&
            currentIsExpression == snapshot.IsExpression &&
            baseValueSourceMatches;
        return (verified, currentValue, currentIsExpression, currentBaseValueSource, null);
    }

    private static object CreateDependencyPropertyVerificationResult(
        StoredDependencyPropertySnapshot snapshot,
        (bool verified, string? currentValue, bool? currentIsExpression, string? currentBaseValueSource, string? skippedReason) verification) =>
        new
        {
            propertyName = snapshot.PropertyName,
            verified = verification.verified,
            expectedValue = snapshot.CurrentValue,
            currentValue = verification.currentValue,
            expectedIsExpression = snapshot.IsExpression,
            currentIsExpression = verification.currentIsExpression,
            expectedBaseValueSource = snapshot.BaseValueSource,
            currentBaseValueSource = verification.currentBaseValueSource,
            verificationSkippedReason = verification.skippedReason
        };

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

    private static bool IsRestoreInterrupted(Exception exception) =>
        exception is OperationCanceledException or TimeoutException;

    private static object CreateRestoreResult(RestoreProgress progress) => new
    {
        success = progress.Warnings.Count == 0,
        restoredDependencyPropertyCount = progress.RestoredDependencyPropertyCount,
        restoredDependencyProperties = progress.RestoredDependencyProperties,
        skippedDependencyPropertyCount = progress.SkippedDependencyProperties.Count,
        skippedDependencyProperties = progress.SkippedDependencyProperties,
        restoredViewModelPropertyCount = progress.RestoredViewModelPropertyCount,
        restoredViewModelProperties = progress.RestoredViewModelProperties,
        skippedViewModelPropertyCount = progress.SkippedViewModelProperties.Count,
        skippedViewModelProperties = progress.SkippedViewModelProperties,
        restoredFocus = progress.RestoredFocus,
        warnings = progress.Warnings
    };

    private static object CreateInterruptedRestoreResult(
        int processId,
        string snapshotId,
        RestoreProgress progress) => new
    {
        success = false,
        error = "Restore state snapshot was interrupted before all restore steps completed.",
        errorCode = "Timeout",
        restoreIncomplete = true,
        stateAfterTimeoutUnknown = true,
        requiresReconnect = true,
        hint = "Restore was interrupted after one or more live operations may have already reached the target process.",
        suggestedAction = "Reconnect, re-read runtime state, then retry restore_state_snapshot with the same snapshotId if restoration is still needed.",
        processId,
        snapshotId,
        restoredDependencyPropertyCount = progress.RestoredDependencyPropertyCount,
        restoredDependencyProperties = progress.RestoredDependencyProperties,
        skippedDependencyPropertyCount = progress.SkippedDependencyProperties.Count,
        skippedDependencyProperties = progress.SkippedDependencyProperties,
        restoredViewModelPropertyCount = progress.RestoredViewModelPropertyCount,
        restoredViewModelProperties = progress.RestoredViewModelProperties,
        skippedViewModelPropertyCount = progress.SkippedViewModelProperties.Count,
        skippedViewModelProperties = progress.SkippedViewModelProperties,
        restoredFocus = progress.RestoredFocus,
        warnings = progress.Warnings
    };

    private sealed class RestoreProgress
    {
        public int RestoredDependencyPropertyCount { get; set; }
        public List<object> RestoredDependencyProperties { get; } = [];
        public List<object> SkippedDependencyProperties { get; } = [];
        public int RestoredViewModelPropertyCount { get; set; }
        public List<object> RestoredViewModelProperties { get; } = [];
        public List<object> SkippedViewModelProperties { get; } = [];
        public bool RestoredFocus { get; set; }
        public List<string> Warnings { get; } = [];
    }
}
