namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class BatchMutateTool
{
    private static bool IsRecoverableTimeoutAfterSnapshot(Exception exception, string? snapshotId) =>
        !string.IsNullOrWhiteSpace(snapshotId)
        && exception is OperationCanceledException or TimeoutException;

    private static object CreateSkippedMutationResult(BatchMutationStep mutation) =>
        new
        {
            index = mutation.Index,
            tool = mutation.Tool,
            label = mutation.Label,
            success = false,
            skipped = true,
            error = "Skipped because an earlier mutation failed."
        };

    private static object CreateTimeoutMutationResult(BatchMutationStep mutation, string error) =>
        new
        {
            index = mutation.Index,
            tool = mutation.Tool,
            label = mutation.Label,
            success = false,
            skipped = false,
            error,
            errorCode = "Timeout",
            stateAfterTimeoutUnknown = true,
            result = (object?)null
        };
}
