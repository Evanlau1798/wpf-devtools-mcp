using System.Text.Json;
using System.Diagnostics;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to wait for a DependencyProperty change using bounded polling.
/// </summary>
public sealed class WaitForDpChangeTool : PipeConnectedToolBase
{
    public WaitForDpChangeTool(SessionManager sessionManager) : base(sessionManager) { }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        var propertyName = ParseStringParam(arguments, "propertyName");
        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        var timeoutMs = ParseIntParam(arguments, "timeoutMs");
        var pollIntervalMs = ParseIntParam(arguments, "pollIntervalMs");
        JsonElement? expectedValue = null;
        if (arguments.HasValue && arguments.Value.TryGetProperty("expectedValue", out var expectedValueProperty))
        {
            expectedValue = expectedValueProperty.Clone();
        }
        JsonElement? triggerMutation = null;
        if (arguments.HasValue && arguments.Value.TryGetProperty("triggerMutation", out var triggerMutationProperty))
        {
            if (triggerMutationProperty.ValueKind != JsonValueKind.Object)
            {
                return CreateInvalidParamError("triggerMutation must be an object when provided.");
            }

            triggerMutation = triggerMutationProperty.Clone();
        }

        const int defaultTimeoutMs = 5000;
        const int defaultPollIntervalMs = 200;
        var effectiveTimeoutMs = timeoutMs ?? defaultTimeoutMs;
        var effectivePollIntervalMs = pollIntervalMs ?? defaultPollIntervalMs;

        if (effectiveTimeoutMs is < 1 or > 30000)
        {
            return CreateInvalidParamError("timeoutMs must be between 1 and 30000.");
        }

        if (effectivePollIntervalMs is < 50 or > 5000)
        {
            return CreateInvalidParamError("pollIntervalMs must be between 50 and 5000.");
        }

        var initialSnapshot = await ReadSnapshotAsync(processId, elementId, propertyName, cancellationToken).ConfigureAwait(false);
        if (initialSnapshot.Error != null)
        {
            return initialSnapshot.Error;
        }

        var matchedExpectedValueAtStart = expectedValue.HasValue &&
            JsonValueMatchesFormatted(expectedValue.Value, initialSnapshot.FormattedValue);
        if (matchedExpectedValueAtStart)
        {
            return BuildWaitResult(
                changed: false,
                timedOut: false,
                propertyName,
                elementId,
                initialSnapshot,
                initialSnapshot,
                elapsedMs: 0,
                pollCount: 0,
                observedChange: false,
                matchedExpectedValueAtStart: true,
                completionReason: "ExpectedValueAlreadySatisfied");
        }

        if (triggerMutation.HasValue)
        {
            var triggerResult = await ExecuteTriggerMutationAsync(
                processId,
                elementId,
                triggerMutation.Value,
                cancellationToken).ConfigureAwait(false);
            if (triggerResult.Error != null)
            {
                return triggerResult.Error;
            }

            var afterTriggerSnapshot = await ReadSnapshotAsync(processId, elementId, propertyName, cancellationToken).ConfigureAwait(false);
            if (afterTriggerSnapshot.Error != null)
            {
                return afterTriggerSnapshot.Error;
            }

            if (HasReachedTarget(initialSnapshot, afterTriggerSnapshot, expectedValue))
            {
                return BuildWaitResult(
                    changed: true,
                    timedOut: false,
                    propertyName,
                    elementId,
                    initialSnapshot,
                    afterTriggerSnapshot,
                    elapsedMs: 0,
                    pollCount: 0,
                    observedChange: HasObservedChange(initialSnapshot, afterTriggerSnapshot),
                    matchedExpectedValueAtStart: false,
                    completionReason: expectedValue.HasValue ? "ExpectedValueReached" : "ValueChanged");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var pollCount = 0;
        while (stopwatch.ElapsedMilliseconds < effectiveTimeoutMs)
        {
            await Task.Delay(effectivePollIntervalMs, cancellationToken).ConfigureAwait(false);
            pollCount++;

            var currentSnapshot = await ReadSnapshotAsync(processId, elementId, propertyName, cancellationToken).ConfigureAwait(false);
            if (currentSnapshot.Error != null)
            {
                return currentSnapshot.Error;
            }

            if (HasReachedTarget(initialSnapshot, currentSnapshot, expectedValue))
            {
                return BuildWaitResult(
                    changed: true,
                    timedOut: false,
                    propertyName,
                    elementId,
                    initialSnapshot,
                    currentSnapshot,
                    stopwatch.ElapsedMilliseconds,
                    pollCount,
                    observedChange: HasObservedChange(initialSnapshot, currentSnapshot),
                    matchedExpectedValueAtStart: false,
                    completionReason: expectedValue.HasValue ? "ExpectedValueReached" : "ValueChanged");
            }
        }

        var finalSnapshot = await ReadSnapshotAsync(processId, elementId, propertyName, cancellationToken).ConfigureAwait(false);
        if (finalSnapshot.Error != null)
        {
            return finalSnapshot.Error;
        }

        if (HasReachedTarget(initialSnapshot, finalSnapshot, expectedValue))
        {
            return BuildWaitResult(
                changed: true,
                timedOut: false,
                propertyName,
                elementId,
                initialSnapshot,
                finalSnapshot,
                stopwatch.ElapsedMilliseconds,
                pollCount,
                observedChange: HasObservedChange(initialSnapshot, finalSnapshot),
                matchedExpectedValueAtStart: false,
                completionReason: expectedValue.HasValue ? "ExpectedValueReached" : "ValueChanged");
        }

        return BuildWaitResult(
            changed: false,
            timedOut: true,
            propertyName,
            elementId,
            initialSnapshot,
            finalSnapshot,
            stopwatch.ElapsedMilliseconds,
            pollCount,
            observedChange: HasObservedChange(initialSnapshot, finalSnapshot),
            matchedExpectedValueAtStart: false,
            completionReason: "TimedOut");
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
        CancellationToken cancellationToken)
    {
        var batchArgs = BuildTriggerBatchArgs(processId, elementId, triggerMutation);
        var result = await new BatchMutateTool(_sessionManager)
            .ExecuteAsync(batchArgs, cancellationToken)
            .ConfigureAwait(false);
        var payload = result is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(result);

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

    private static bool HasReachedTarget(DpSnapshot initialSnapshot, DpSnapshot currentSnapshot, JsonElement? expectedValue)
    {
        if (expectedValue.HasValue)
        {
            return JsonValueMatchesFormatted(expectedValue.Value, currentSnapshot.FormattedValue);
        }

        return HasObservedChange(initialSnapshot, currentSnapshot);
    }

    private static bool HasObservedChange(DpSnapshot initialSnapshot, DpSnapshot currentSnapshot)
    {
        return !string.Equals(initialSnapshot.FormattedValue, currentSnapshot.FormattedValue, StringComparison.Ordinal) ||
               !string.Equals(initialSnapshot.BaseValueSource, currentSnapshot.BaseValueSource, StringComparison.Ordinal);
    }

    private static bool JsonValueMatchesFormatted(JsonElement expectedValue, string? formattedValue)
    {
        return expectedValue.ValueKind switch
        {
            JsonValueKind.String => string.Equals(expectedValue.GetString(), formattedValue, StringComparison.Ordinal),
            JsonValueKind.True => string.Equals("True", formattedValue, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.False => string.Equals("False", formattedValue, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Null => formattedValue == null,
            JsonValueKind.Number => string.Equals(expectedValue.GetRawText(), formattedValue, StringComparison.Ordinal),
            _ => string.Equals(expectedValue.GetRawText(), formattedValue, StringComparison.Ordinal)
        };
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
        string completionReason)
    {
        return new
        {
            success = true,
            changed,
            timedOut,
            observedChange,
            matchedExpectedValueAtStart,
            completionReason,
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

    private readonly record struct DpSnapshot(string? FormattedValue, string BaseValueSource, object? Error = null)
    {
        public static DpSnapshot FromError(object error) => new(null, string.Empty, error);
    }

    private readonly record struct TriggerMutationResult(object? Error = null)
    {
        public static TriggerMutationResult Success => new();
    }
}
