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
        var steps = new List<object>();
        if (progress.FailedDependencyPropertyRestores.Count > 0)
        {
            steps.AddRange(CreateDependencyPropertyRestoreNextSteps(progress));
        }

        var viewModelFailure = progress.FailedViewModelRestores.FirstOrDefault()
            ?? progress.SkippedViewModelRestores.FirstOrDefault(static failure =>
                string.Equals(failure.RestoreDisposition, "SkippedComplexReference", StringComparison.Ordinal)
                || !failure.Verified);
        if (viewModelFailure != null)
        {
            steps.AddRange(CreateViewModelRestoreNextSteps(viewModelFailure));
        }

        return steps.ToArray();
    }

    private static object[] CreateDependencyPropertyRestoreNextSteps(RestoreProgress progress)
    {
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

    private static object[] CreateViewModelRestoreNextSteps(ViewModelRestoreFailure failure)
    {
        var reason = string.Equals(failure.RestoreDisposition, "SkippedComplexReference", StringComparison.Ordinal)
            ? $"ViewModel property '{failure.PropertyName}' was skipped as a complex reference; modify_viewmodel cannot reconstruct object identity from the captured snapshot value."
            : $"ViewModel property '{failure.PropertyName}' was not verified after restore_state_snapshot.";

        return
        [
            new
            {
                tool = "get_viewmodel",
                @params = new { elementId = failure.ElementId },
                reason,
                expectedOutcome = "Confirm the current value, type, and canWrite status before choosing an app-specific recovery path."
            },
            new
            {
                tool = "capture_state_snapshot",
                @params = new { elementId = failure.ElementId, viewModelPropertyNames = new[] { failure.PropertyName } },
                reason = $"Before mutating '{failure.PropertyName}' again, capture a fresh snapshot and prefer app commands or UI selection when the value is a complex reference.",
                expectedOutcome = "A fresh baseline documents whether the property is rollback-safe or requires app-specific recovery."
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
        public List<ViewModelRestoreFailure> SkippedViewModelRestores { get; } = [];
        public List<ViewModelRestoreFailure> FailedViewModelRestores { get; } = [];
        public bool RestoredFocus { get; set; }
        public List<string> Warnings { get; } = [];
        public List<FailedDependencyPropertyRestore> FailedDependencyPropertyRestores { get; } = [];
    }

    private sealed record FailedDependencyPropertyRestore(string? ElementId, string PropertyName);

    private sealed record ViewModelRestoreFailure(
        string? ElementId,
        string PropertyName,
        string RestoreDisposition,
        string Reason,
        bool Verified);
}
