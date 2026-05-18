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

    private object? BuildRecovery(int processId, string? capturedSnapshotId)
    {
        if (!TryGetRetainedRollbackSnapshotId(processId, capturedSnapshotId, out var snapshotId, out var reason))
        {
            return new
            {
                suggestedAction = "Inspect the failed batch_mutate response and manually reverse any completed mutations.",
                hint = reason
            };
        }

        return new
        {
            suggestedAction = $"Call restore_state_snapshot with snapshotId '{snapshotId}' before retrying batch_mutate.",
            hint = "The batch may have left runtime state partially changed.",
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
        string error) =>
        new(error, errorCode, BuildRecovery(processId, snapshotId));

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

    private sealed record BatchMutationFailure(string Error, string ErrorCode, object? Recovery);
}
