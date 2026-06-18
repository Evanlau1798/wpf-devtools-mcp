using System.Text.Json;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class RestoreStateSnapshotTool
{
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
        warnings = progress.Warnings,
        nextSteps = CreateRestoreNextSteps(progress)
    };

    private static void AddDependencyPropertyVerificationFailure(
        RestoreProgress progress,
        StoredDependencyPropertySnapshot snapshot)
    {
        progress.Warnings.Add($"DependencyProperty restore verification failed for '{snapshot.PropertyName}'.");
        progress.FailedDependencyPropertyRestores.Add(new(snapshot.ElementId, snapshot.PropertyName));
    }

    private static object[] CreateRestoreNextSteps(RestoreProgress progress)
    {
        if (progress.FailedDependencyPropertyRestores.Count == 0)
        {
            return [];
        }

        var firstFailure = progress.FailedDependencyPropertyRestores[0];
        var propertyNames = progress.FailedDependencyPropertyRestores
            .Select(failure => failure.PropertyName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return
        [
            new
            {
                tool = "get_binding_value_chain",
                @params = new { elementId = firstFailure.ElementId, propertyName = firstFailure.PropertyName },
                reason = "Restore verification failed after replaying a DependencyProperty snapshot; inspect the binding chain to identify the backing ViewModel source.",
                expectedOutcome = "Identify the source property that also needs to be captured for deterministic rollback."
            },
            new
            {
                tool = "capture_state_snapshot",
                @params = new { elementId = firstFailure.ElementId, propertyNames },
                reason = "For two-way binding targets, capture the DependencyProperty and the backing ViewModel property before mutating.",
                expectedOutcome = "A later restore_state_snapshot can replay both the target property and source value."
            }
        ];
    }

    private static object CreateInterruptedRestoreResult(
        int processId,
        string snapshotId,
        RestoreProgress progress,
        JsonElement? recoveryResponse = null)
    {
        var recovery = recoveryResponse.HasValue
            ? ToolRecoveryPayload.Extract(recoveryResponse.Value)
            : new ToolRecoveryProjection(null, null, null, null, true, true, processId, null, null, null, null, null);
        var timeoutOrTransportRecovery = !recoveryResponse.HasValue
            || ToolRecoveryPayload.IsTimeoutOrTransportRecovery(recoveryResponse.Value);
        var defaultStateAfterTimeoutUnknown = timeoutOrTransportRecovery ? true : (bool?)null;
        var defaultRequiresReconnect = timeoutOrTransportRecovery ? true : (bool?)null;

        return new
        {
            success = false,
            error = recovery.Error ?? "Restore state snapshot was interrupted before all restore steps completed.",
            errorCode = recovery.ErrorCode ?? "Timeout",
            restoreIncomplete = true,
            stateAfterTimeoutUnknown = recovery.StateAfterTimeoutUnknown ?? defaultStateAfterTimeoutUnknown,
            requiresReconnect = recovery.RequiresReconnect ?? defaultRequiresReconnect,
            hint = recovery.Hint ?? "Restore was interrupted before all snapshot state could be verified.",
            suggestedAction = recovery.SuggestedAction
                ?? recovery.RetryAfter
                ?? "Reconnect, re-read runtime state, then retry restore_state_snapshot with the same snapshotId if restoration is still needed.",
            processId = recovery.ProcessId ?? processId,
            timeoutSeconds = recovery.TimeoutSeconds,
            retryAfterSeconds = recovery.RetryAfterSeconds,
            retryAfter = recovery.RetryAfter,
            availableTokens = recovery.AvailableTokens,
            availableEvents = recovery.AvailableEvents,
            recovery = recovery.ToRecovery(),
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
    }

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
        public List<FailedDependencyPropertyRestore> FailedDependencyPropertyRestores { get; } = [];
    }

    private sealed record FailedDependencyPropertyRestore(string? ElementId, string PropertyName);
}
