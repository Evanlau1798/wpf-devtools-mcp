namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class BatchMutateTool
{
    private object? BuildRollback(int processId, string? capturedSnapshotId, bool shouldOfferRollback)
    {
        if (!shouldOfferRollback)
        {
            return null;
        }

        if (!TryGetRetainedRollbackSnapshotId(processId, capturedSnapshotId, out var snapshotId, out var reason))
        {
            return new
            {
                available = false,
                reason
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
            }
        };
    }

    private object? BuildRecovery(
        int processId,
        string? capturedSnapshotId,
        ToolRecoveryProjection? projection)
    {
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
        ToolRecoveryProjection? projection) =>
        new(error, errorCode, BuildRecovery(processId, snapshotId, projection), projection);

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
}
