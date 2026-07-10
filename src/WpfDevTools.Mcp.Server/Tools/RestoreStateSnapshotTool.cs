using System.Text.Json;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class RestoreStateSnapshotTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
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
                Hint = "Take a fresh snapshot with capture_state_snapshot for the current connected process, or verify the snapshotId has not expired, was not removed by a prior restore, and belongs to this session. If restore conflicts persist, inspect live state with get_dp_value_source and get_bindings before retrying."
            };
        }

        var sessionGeneration = snapshot.SessionGeneration;
        var progress = new RestoreProgress();
        try
        {
            await RestoreViewModelPropertiesAsync(
                processId,
                sessionGeneration,
                snapshot.ViewModelProperties,
                progress,
                cancellationToken).ConfigureAwait(false);
            await RestoreDependencyPropertiesAsync(
                processId,
                sessionGeneration,
                snapshot.DependencyProperties,
                progress,
                cancellationToken).ConfigureAwait(false);
            progress.RestoredFocus = await RestoreFocusAsync(
                processId,
                sessionGeneration,
                snapshot.Focus,
                progress.Warnings,
                cancellationToken).ConfigureAwait(false);
        }
        catch (StructuredRestoreFailureException ex)
        {
            progress.Warnings.Add("Restore received a timeout or transport recovery payload before all snapshot state could be verified.");
            return CreateInterruptedRestoreResult(processId, snapshotId, progress, ex.Response);
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
        long sessionGeneration,
        IReadOnlyList<StoredDependencyPropertySnapshot> snapshots,
        RestoreProgress progress,
        CancellationToken cancellationToken)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.IsExpression && !string.IsNullOrWhiteSpace(snapshot.ExpressionRestoreToken))
            {
                var restoreResponse = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                    processId,
                    sessionGeneration,
                    "restore_dp_expression",
                    new
                    {
                        elementId = snapshot.ElementId,
                        propertyName = snapshot.PropertyName,
                        restoreToken = snapshot.ExpressionRestoreToken,
                        targetValue = snapshot.CurrentValue
                    },
                    cancellationToken,
                    piggybackPendingEvents: false).ConfigureAwait(false));

                if (IsSuccess(restoreResponse))
                {
                    var verification = await VerifyDependencyPropertyAsync(
                        processId,
                        sessionGeneration,
                        snapshot,
                        cancellationToken).ConfigureAwait(false);
                    progress.RestoredDependencyProperties.Add(CreateDependencyPropertyVerificationResult(snapshot, verification));
                    if (!verification.verified)
                    {
                        AddDependencyPropertyVerificationFailure(progress, snapshot);
                    }

                    progress.RestoredDependencyPropertyCount++;
                    continue;
                }

                ThrowIfStructuredRestoreFailure(restoreResponse);
                progress.Warnings.Add($"DependencyProperty restore failed for '{snapshot.PropertyName}'.");
                continue;
            }

            if (!snapshot.CanRestore)
            {
                var verification = await VerifyDependencyPropertyAsync(
                    processId,
                    sessionGeneration,
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
                    AddDependencyPropertyVerificationFailure(progress, snapshot);
                }

                continue;
            }

            object parameters = snapshot.HadLocalValue
                ? new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName, value = snapshot.LocalValue }
                : new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName };
            var method = snapshot.HadLocalValue ? "set_dp_value" : "clear_dp_value";
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                sessionGeneration,
                method,
                parameters,
                cancellationToken,
                piggybackPendingEvents: false).ConfigureAwait(false));

            if (IsSuccess(response))
            {
                var verification = await VerifyDependencyPropertyAsync(
                    processId,
                    sessionGeneration,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                if (ShouldRetryClear(snapshot, verification))
                {
                    var retryResponse = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                        processId,
                        sessionGeneration,
                        "clear_dp_value",
                        new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName },
                        cancellationToken,
                        piggybackPendingEvents: false).ConfigureAwait(false));
                    ThrowIfStructuredRestoreFailure(retryResponse);
                    if (IsSuccess(retryResponse))
                    {
                        verification = await VerifyDependencyPropertyAsync(
                            processId,
                            sessionGeneration,
                            snapshot,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                progress.RestoredDependencyProperties.Add(CreateDependencyPropertyVerificationResult(snapshot, verification));
                if (!verification.verified)
                {
                    AddDependencyPropertyVerificationFailure(progress, snapshot);
                }

                progress.RestoredDependencyPropertyCount++;
                continue;
            }

            ThrowIfStructuredRestoreFailure(response);
            progress.Warnings.Add($"DependencyProperty restore failed for '{snapshot.PropertyName}'.");
        }
    }

    private async Task RestoreViewModelPropertiesAsync(
        int processId,
        long sessionGeneration,
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
                    sessionGeneration,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                var restoreDisposition = ClassifyRestoreDisposition(snapshot);
                var reason = snapshot.SkipReason ?? $"Property '{snapshot.PropertyName}' is not writable.";
                progress.SkippedViewModelProperties.Add(new
                {
                    propertyName = snapshot.PropertyName,
                    reason,
                    restoreDisposition,
                    verified = verification.verified,
                    expectedValue = snapshot.Value,
                    currentValue = verification.currentValue,
                    verificationSkippedReason = verification.skippedReason
                });
                progress.SkippedViewModelRestores.Add(new(
                    snapshot.ElementId,
                    snapshot.PropertyName,
                    restoreDisposition,
                    reason,
                    verification.verified));

                if (!verification.verified)
                {
                    progress.Warnings.Add($"ViewModel restore verification failed for skipped property '{snapshot.PropertyName}'.");
                }

                continue;
            }

            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                sessionGeneration,
                "modify_viewmodel",
                new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName, value = snapshot.Value },
                cancellationToken,
                piggybackPendingEvents: false).ConfigureAwait(false));

            if (IsSuccess(response))
            {
                var verification = await VerifyViewModelPropertyAsync(
                    processId,
                    sessionGeneration,
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
                    progress.FailedViewModelRestores.Add(new(
                        snapshot.ElementId,
                        snapshot.PropertyName,
                        "ReadBackMismatch",
                        "modify_viewmodel reported success but get_viewmodel did not read back the captured value.",
                        false));
                }

                progress.RestoredViewModelPropertyCount++;
                continue;
            }

            ThrowIfStructuredRestoreFailure(response);
            progress.Warnings.Add($"ViewModel restore failed for '{snapshot.PropertyName}'.");
        }
    }

    private async Task<(bool verified, string? currentValue, string? skippedReason)> VerifyViewModelPropertyAsync(
        int processId,
        long sessionGeneration,
        StoredViewModelPropertySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            sessionGeneration,
            "get_viewmodel",
            new { elementId = snapshot.ElementId },
            cancellationToken,
            piggybackPendingEvents: false).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            ThrowIfStructuredRestoreFailure(response);
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
        long sessionGeneration,
        StoredFocusSnapshot? snapshot,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (snapshot?.FocusedElementId == null)
        {
            return false;
        }

        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            sessionGeneration,
            "focus_element",
            new { elementId = snapshot.FocusedElementId },
            cancellationToken,
            piggybackPendingEvents: false).ConfigureAwait(false));

        if (IsSuccess(response))
        {
            return true;
        }

        ThrowIfStructuredRestoreFailure(response);
        warnings.Add("Focus restore failed.");
        return false;
    }

    private async Task<(bool verified, string? currentValue, bool? currentIsExpression, string? currentBaseValueSource, string? skippedReason)> VerifyDependencyPropertyAsync(
        int processId,
        long sessionGeneration,
        StoredDependencyPropertySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            sessionGeneration,
            "get_dp_value_source",
            new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName },
            cancellationToken,
            piggybackPendingEvents: false).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            ThrowIfStructuredRestoreFailure(response);
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

    private static bool ShouldRetryClear(
        StoredDependencyPropertySnapshot snapshot,
        (bool verified, string? currentValue, bool? currentIsExpression, string? currentBaseValueSource, string? skippedReason) verification) =>
        !snapshot.HadLocalValue
        && !snapshot.IsExpression
        && !verification.verified
        && verification.currentIsExpression == false
        && string.Equals(verification.currentValue, snapshot.CurrentValue, StringComparison.Ordinal)
        && string.Equals(verification.currentBaseValueSource, "LocalValue", StringComparison.Ordinal);

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

    private static void ThrowIfStructuredRestoreFailure(JsonElement response)
    {
        if (ToolRecoveryPayload.IsTimeoutOrTransportRecovery(response)
            || ToolRecoveryPayload.HasRecoveryGuidance(response))
        {
            throw new StructuredRestoreFailureException(response.Clone());
        }
    }

    private sealed class StructuredRestoreFailureException(JsonElement response) : Exception
    {
        public JsonElement Response { get; } = response;
    }
}
