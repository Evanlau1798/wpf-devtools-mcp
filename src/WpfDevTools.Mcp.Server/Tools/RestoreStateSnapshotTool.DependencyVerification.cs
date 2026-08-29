using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class RestoreStateSnapshotTool
{
    private async Task VerifyDependencyPropertiesAsync(
        int processId,
        long sessionGeneration,
        RestoreProgress progress,
        CancellationToken cancellationToken)
    {
        foreach (var snapshot in progress.RestoredDependencyPropertySnapshots)
        {
            var verification = await VerifyDependencyPropertyAsync(
                processId,
                sessionGeneration,
                snapshot,
                cancellationToken).ConfigureAwait(false);
            progress.RestoredDependencyProperties.Add(
                CreateDependencyPropertyVerificationResult(snapshot, verification));
            if (!verification.verified)
            {
                AddDependencyPropertyVerificationFailure(progress, snapshot);
            }
        }

        foreach (var snapshot in progress.SkippedDependencyPropertySnapshots)
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
        }
    }
}
