using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class BatchMutateTool
{
    private object? BuildRollback(
        int processId,
        string? capturedSnapshotId,
        bool shouldOfferRollback,
        AutomaticRollbackStatus? automaticRollback)
    {
        if (!shouldOfferRollback)
        {
            return null;
        }

        if (automaticRollback?.Succeeded == true)
        {
            return new
            {
                available = false,
                reason = automaticRollback.Reason,
                rollbackOnFailure = true,
                attempted = true,
                succeeded = true,
                result = automaticRollback.Result
            };
        }

        if (!TryGetRetainedRollbackSnapshotId(processId, capturedSnapshotId, out var snapshotId, out var reason))
        {
            return new
            {
                available = false,
                reason = automaticRollback?.Reason ?? reason,
                rollbackOnFailure = automaticRollback?.Requested,
                attempted = automaticRollback?.Attempted,
                succeeded = automaticRollback?.Succeeded,
                result = automaticRollback?.Result
            };
        }

        return new
        {
            available = true,
            snapshotId,
            tool = "restore_state_snapshot",
            @params = new
            {
                processId,
                snapshotId
            },
            rollbackOnFailure = automaticRollback?.Requested,
            attempted = automaticRollback?.Attempted,
            succeeded = automaticRollback?.Succeeded,
            result = automaticRollback?.Result,
            reason = automaticRollback?.Reason
        };
    }

    private object? BuildRecovery(
        int processId,
        string? capturedSnapshotId,
        ToolRecoveryProjection? projection,
        AutomaticRollbackStatus? automaticRollback)
    {
        if (automaticRollback?.Succeeded == true)
        {
            return new
            {
                suggestedAction = "Automatic rollback completed. Inspect current state before retrying batch_mutate.",
                hint = automaticRollback.Reason,
                rollbackOnFailure = true,
                attempted = true,
                succeeded = true
            };
        }

        if (automaticRollback?.Attempted == true && automaticRollback.Succeeded == false)
        {
            return new
            {
                suggestedAction = "Automatic rollback failed. Inspect rollback.result and manually verify runtime state before retrying.",
                hint = automaticRollback.Reason,
                rollbackOnFailure = true,
                attempted = true,
                succeeded = false,
                requiresReconnect = projection?.RequiresReconnect,
                stateAfterTimeoutUnknown = projection?.StateAfterTimeoutUnknown,
                processId = projection?.ProcessId,
                timeoutSeconds = projection?.TimeoutSeconds,
                retryAfterSeconds = projection?.RetryAfterSeconds,
                retryAfter = projection?.RetryAfter,
                availableTokens = projection?.AvailableTokens,
                availableEvents = projection?.AvailableEvents
            };
        }

        if (!TryGetRetainedRollbackSnapshotId(processId, capturedSnapshotId, out var snapshotId, out var reason))
        {
            return new
            {
                suggestedAction = projection?.SuggestedAction
                    ?? "Inspect the failed batch_mutate response and manually reverse any completed mutations.",
                hint = projection?.Hint ?? reason,
                requiresReconnect = projection?.RequiresReconnect,
                stateAfterTimeoutUnknown = projection?.StateAfterTimeoutUnknown,
                processId = projection?.ProcessId,
                timeoutSeconds = projection?.TimeoutSeconds,
                retryAfterSeconds = projection?.RetryAfterSeconds,
                retryAfter = projection?.RetryAfter,
                availableTokens = projection?.AvailableTokens,
                availableEvents = projection?.AvailableEvents
            };
        }

        return new
        {
            suggestedAction = $"Call restore_state_snapshot with snapshotId '{snapshotId}' before retrying batch_mutate.",
            hint = projection?.Hint ?? "The batch may have left runtime state partially changed.",
            requiresReconnect = projection?.RequiresReconnect,
            stateAfterTimeoutUnknown = projection?.StateAfterTimeoutUnknown,
            processId = projection?.ProcessId,
            timeoutSeconds = projection?.TimeoutSeconds,
            retryAfterSeconds = projection?.RetryAfterSeconds,
            retryAfter = projection?.RetryAfter,
            availableTokens = projection?.AvailableTokens,
            availableEvents = projection?.AvailableEvents,
            tool = "restore_state_snapshot",
            @params = new
            {
                processId,
                snapshotId
            }
        };
    }

    private BatchMutationFailure BuildBatchFailure(
        int processId,
        string? snapshotId,
        string errorCode,
        string error,
        ToolRecoveryProjection? projection,
        AutomaticRollbackStatus? automaticRollback) =>
        new(error, errorCode, BuildRecovery(processId, snapshotId, projection, automaticRollback), projection);

    private bool TryGetRetainedRollbackSnapshotId(
        int processId,
        string? capturedSnapshotId,
        out string? snapshotId,
        out string reason)
    {
        if (!string.IsNullOrWhiteSpace(capturedSnapshotId))
        {
            if (_sessionManager.TryGetStateSnapshot(processId, capturedSnapshotId, out _))
            {
                snapshotId = capturedSnapshotId;
                reason = string.Empty;
                return true;
            }

            snapshotId = null;
            reason = "The captured snapshot is no longer retained for automatic rollback guidance.";
            return false;
        }

        if (_sessionManager.TryGetActiveSnapshotId(processId, out snapshotId))
        {
            reason = string.Empty;
            return true;
        }

        reason = "No active snapshot is available for rollback guidance.";
        return false;
    }

    private sealed record BatchMutationFailure(
        string Error,
        string ErrorCode,
        object? Recovery,
        ToolRecoveryProjection? Projection);

    private sealed record AutomaticRollbackStatus(
        bool Requested,
        bool Attempted,
        bool Succeeded,
        JsonElement? Result,
        string Reason)
    {
        public static AutomaticRollbackStatus Skipped(string reason) =>
            new(Requested: true, Attempted: false, Succeeded: false, Result: null, Reason: reason);

        public static AutomaticRollbackStatus Completed(bool succeeded, JsonElement result, string reason) =>
            new(Requested: true, Attempted: true, Succeeded: succeeded, Result: result, Reason: reason);
    }
}
