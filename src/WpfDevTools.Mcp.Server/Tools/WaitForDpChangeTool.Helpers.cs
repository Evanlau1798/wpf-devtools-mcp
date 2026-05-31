using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class WaitForDpChangeTool
{
    private async Task<DpSnapshot?> TryReadFinalSnapshotAsync(
        int processId,
        string? elementId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        using var finalReadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        finalReadCts.CancelAfter(FinalSnapshotTimeoutMs);

        try
        {
            return await ReadSnapshotAsync(
                processId,
                elementId,
                propertyName,
                finalReadCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<DpSnapshot> ReadSnapshotAsync(
        int processId,
        string? elementId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var result = await SendInspectorRequestWithoutPiggybackAsync(
            processId,
            "get_dp_value_source",
            new
            {
                elementId,
                propertyName,
                compact = true,
                settleBindings = true
            },
            cancellationToken).ConfigureAwait(false);

        var payload = result is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(result);
        if (!IsSuccessfulSnapshotPayload(payload))
        {
            return DpSnapshot.FromError(result);
        }

        return new DpSnapshot(
            FormattedValue: TryGetStringProperty(payload, "currentValue")
                ?? TryGetStringProperty(payload, "effectiveValue"),
            BaseValueSource: TryGetStringProperty(payload, "baseValueSource") ?? string.Empty);
    }

    private async Task<TriggerMutationResult> ExecuteTriggerMutationAsync(
        int processId,
        string? elementId,
        JsonElement triggerMutation,
        long expectedSessionGeneration,
        int remainingBudgetMs,
        CancellationToken cancellationToken)
    {
        if (remainingBudgetMs <= 0)
        {
            if (_triggerMutationTimeoutRequiresReconnect)
            {
                _sessionManager.GetPipeClient(processId, expectedSessionGeneration)?.Dispose();
            }

            return TriggerMutationResult.Timeout(
                stateAfterTimeoutUnknown: true,
                requiresReconnect: _triggerMutationTimeoutRequiresReconnect);
        }

        var batchArgs = BuildTriggerBatchArgs(processId, elementId, triggerMutation);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(remainingBudgetMs));

        JsonElement payload;
        try
        {
            var result = await _triggerMutationExecutor(batchArgs, timeoutCts.Token)
                .ConfigureAwait(false);
            payload = result is JsonElement jsonElement
                ? jsonElement
                : JsonSerializer.SerializeToElement(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            if (_triggerMutationTimeoutRequiresReconnect)
            {
                _sessionManager.GetPipeClient(processId, expectedSessionGeneration)?.Dispose();
            }

            return TriggerMutationResult.Timeout(
                stateAfterTimeoutUnknown: true,
                requiresReconnect: _triggerMutationTimeoutRequiresReconnect);
        }

        return IsSuccessfulSnapshotPayload(payload)
            ? TriggerMutationResult.Success
            : new TriggerMutationResult(payload.Clone());
    }

    private static JsonElement BuildTriggerBatchArgs(int processId, string? elementId, JsonElement triggerMutation)
    {
        var payload = new Dictionary<string, object?>
        {
            ["processId"] = processId,
            ["mutations"] = new[] { triggerMutation.Clone() }
        };

        var hasArgsElement = triggerMutation.TryGetProperty("args", out var argsElement);
        if (!string.IsNullOrWhiteSpace(elementId) && !hasArgsElement)
        {
            payload["elementId"] = elementId;
        }
        else if (!string.IsNullOrWhiteSpace(elementId)
            && hasArgsElement
            && argsElement.ValueKind == JsonValueKind.Object
            && !argsElement.TryGetProperty("elementId", out _))
        {
            payload["elementId"] = elementId;
        }

        return JsonSerializer.SerializeToElement(payload);
    }

    private static bool IsSuccessfulSnapshotPayload(JsonElement payload)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("success", out var successProperty)
            && successProperty.ValueKind == JsonValueKind.True;
    }

    private static string? TryGetStringProperty(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool HasObservedChange(DpSnapshot initialSnapshot, DpSnapshot currentSnapshot)
    {
        return !string.Equals(initialSnapshot.FormattedValue, currentSnapshot.FormattedValue, StringComparison.Ordinal) ||
               !string.Equals(initialSnapshot.BaseValueSource, currentSnapshot.BaseValueSource, StringComparison.Ordinal);
    }

    private static object BuildWaitResult(
        bool changed,
        bool timedOut,
        string propertyName,
        string? elementId,
        DpSnapshot initialSnapshot,
        DpSnapshot currentSnapshot,
        long elapsedMs,
        int pollCount,
        bool observedChange,
        bool matchedExpectedValueAtStart,
        string completionReason,
        bool stateAfterTimeoutUnknown = false,
        bool requiresReconnect = false)
    {
        return new
        {
            success = true,
            changed,
            timedOut,
            observedChange,
            matchedExpectedValueAtStart,
            completionReason,
            stateAfterTimeoutUnknown,
            requiresReconnect,
            elementId,
            propertyName,
            initialValue = initialSnapshot.FormattedValue,
            initialBaseValueSource = initialSnapshot.BaseValueSource,
            currentValue = currentSnapshot.FormattedValue,
            baseValueSource = currentSnapshot.BaseValueSource,
            elapsedMs,
            pollCount
        };
    }
}
