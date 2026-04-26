namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class BatchMutateTool
{
    private object? BuildRollback(int processId, string? capturedSnapshotId, bool shouldOfferRollback)
    {
        if (!shouldOfferRollback)
        {
            return null;
        }

        var snapshotId = capturedSnapshotId;
        if (string.IsNullOrWhiteSpace(snapshotId)
            && !_sessionManager.TryGetActiveSnapshotId(processId, out snapshotId))
        {
            return new
            {
                available = false,
                reason = "No active snapshot is available for rollback guidance."
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
        var snapshotId = capturedSnapshotId;
        if (string.IsNullOrWhiteSpace(snapshotId)
            && !_sessionManager.TryGetActiveSnapshotId(processId, out snapshotId))
        {
            return new
            {
                suggestedAction = "Inspect the failed batch_mutate response and manually reverse any completed mutations.",
                hint = "No active snapshot is available for automatic rollback guidance."
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

    private sealed record BatchMutationFailure(string Error, string ErrorCode, object? Recovery);
}
