using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to wait for a DependencyProperty change using bounded polling.
/// </summary>
public sealed partial class WaitForDpChangeTool : PipeConnectedToolBase
{
    private const int FinalSnapshotTimeoutMs = 1000;
    private static readonly AsyncLocal<Func<Task>?> BeforePollDelayForTestingValue = new();

    internal static Func<Task>? BeforePollDelayForTesting
    {
        get => BeforePollDelayForTestingValue.Value;
        set => BeforePollDelayForTestingValue.Value = value;
    }

    private readonly Func<JsonElement, CancellationToken, Task<object>> _triggerMutationExecutor;
    private readonly bool _triggerMutationTimeoutRequiresReconnect;

    public WaitForDpChangeTool(SessionManager sessionManager)
        : this(sessionManager, triggerMutationExecutor: null, triggerMutationTimeoutRequiresReconnect: true)
    {
    }

    internal WaitForDpChangeTool(
        SessionManager sessionManager,
        Func<JsonElement, CancellationToken, Task<object>>? triggerMutationExecutor,
        bool triggerMutationTimeoutRequiresReconnect = true)
        : base(sessionManager)
    {
        _triggerMutationExecutor = triggerMutationExecutor
            ?? ((batchArgs, cancellationToken) => new BatchMutateTool(sessionManager).ExecuteAsync(batchArgs, cancellationToken));
        _triggerMutationTimeoutRequiresReconnect = triggerMutationTimeoutRequiresReconnect;
    }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        if (!_sessionManager.TryGetSessionGeneration(processId, out var expectedSessionGeneration))
        {
            return CreateNotConnectedError(processId);
        }

        var propertyName = ParseStringParam(arguments, "propertyName");
        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
            arguments,
            "timeoutMs",
            DpChangeWaitLimits.MinTimeoutMs,
            DpChangeWaitLimits.MaxTimeoutMs,
            out var timeoutMs,
            out var timeoutMsError))
        {
            return timeoutMsError!;
        }

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
            arguments,
            "pollIntervalMs",
            DpChangeWaitLimits.MinPollIntervalMs,
            DpChangeWaitLimits.MaxPollIntervalMs,
            out var pollIntervalMs,
            out var pollIntervalMsError))
        {
            return pollIntervalMsError!;
        }
        JsonElement? expectedValue = null;
        if (arguments.HasValue && arguments.Value.TryGetProperty("expectedValue", out var expectedValueProperty))
        {
            expectedValue = expectedValueProperty.Clone();
        }
        JsonElement? triggerMutation = null;
        var parsedTriggerMutation = default(JsonElement);
        var hasTriggerMutation = false;
        if (arguments.HasValue
            && !JsonCompatibilityPayloadParser.TryParseOptionalObjectProperty(
                arguments.Value,
                "triggerMutation",
                out parsedTriggerMutation,
                out hasTriggerMutation,
                out var triggerMutationError))
        {
            return CreateInvalidParamError(triggerMutationError!);
        }

        if (arguments.HasValue && hasTriggerMutation)
        {
            triggerMutation = parsedTriggerMutation;
        }

        var effectiveTimeoutMs = timeoutMs ?? DpChangeWaitLimits.DefaultTimeoutMs;
        var effectivePollIntervalMs = pollIntervalMs ?? DpChangeWaitLimits.DefaultPollIntervalMs;

        if (effectiveTimeoutMs is < DpChangeWaitLimits.MinTimeoutMs or > DpChangeWaitLimits.MaxTimeoutMs)
        {
            return CreateInvalidParamError(
                $"timeoutMs must be between {DpChangeWaitLimits.MinTimeoutMs} and {DpChangeWaitLimits.MaxTimeoutMs}.");
        }

        if (effectivePollIntervalMs is < DpChangeWaitLimits.MinPollIntervalMs or > DpChangeWaitLimits.MaxPollIntervalMs)
        {
            return CreateInvalidParamError(
                $"pollIntervalMs must be between {DpChangeWaitLimits.MinPollIntervalMs} and {DpChangeWaitLimits.MaxPollIntervalMs}.");
        }

        var stopwatch = Stopwatch.StartNew();
        var initialSnapshot = await ReadSnapshotAsync(
            processId,
            elementId,
            propertyName,
            cancellationToken,
            countAgainstRateLimit: true).ConfigureAwait(false);
        if (initialSnapshot.Error != null)
        {
            return initialSnapshot.Error;
        }

        var latestSnapshot = initialSnapshot;
        var matchedExpectedValueAtStart = expectedValue.HasValue &&
            JsonValueMatchesFormatted(expectedValue.Value, initialSnapshot.FormattedValue);
        var observedChangeSinceStart = false;
        if (matchedExpectedValueAtStart && !triggerMutation.HasValue)
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
                expectedSessionGeneration,
                GetRemainingBudgetMs(effectiveTimeoutMs, stopwatch),
                cancellationToken).ConfigureAwait(false);
            if (triggerResult.Error != null)
            {
                return triggerResult.Error;
            }

            if (triggerResult.TimedOut)
            {
                return BuildWaitResult(
                    changed: false,
                    timedOut: true,
                    propertyName,
                    elementId,
                    initialSnapshot,
                    initialSnapshot,
                    elapsedMs: stopwatch.ElapsedMilliseconds,
                    pollCount: 0,
                    observedChange: observedChangeSinceStart,
                    matchedExpectedValueAtStart,
                    completionReason: "TriggerMutationTimedOut",
                    stateAfterTimeoutUnknown: triggerResult.StateAfterTimeoutUnknown,
                    requiresReconnect: triggerResult.RequiresReconnect);
            }

            var afterTriggerSnapshot = await ReadSnapshotAsync(
                processId,
                elementId,
                propertyName,
                cancellationToken,
                countAgainstRateLimit: false).ConfigureAwait(false);
            if (afterTriggerSnapshot.Error != null)
            {
                return afterTriggerSnapshot.Error;
            }

            observedChangeSinceStart |= HasObservedChange(initialSnapshot, afterTriggerSnapshot);
            latestSnapshot = afterTriggerSnapshot;

            if (stopwatch.ElapsedMilliseconds >= effectiveTimeoutMs)
            {
                return BuildWaitResult(
                    changed: false,
                    timedOut: true,
                    propertyName,
                    elementId,
                    initialSnapshot,
                    afterTriggerSnapshot,
                    elapsedMs: stopwatch.ElapsedMilliseconds,
                    pollCount: 0,
                    observedChange: observedChangeSinceStart,
                    matchedExpectedValueAtStart,
                    completionReason: "TimedOut");
            }

            if (HasReachedTarget(initialSnapshot, afterTriggerSnapshot, expectedValue, matchedExpectedValueAtStart, observedChangeSinceStart))
            {
                return BuildWaitResult(
                    changed: true,
                    timedOut: false,
                    propertyName,
                    elementId,
                    initialSnapshot,
                    afterTriggerSnapshot,
                    elapsedMs: stopwatch.ElapsedMilliseconds,
                    pollCount: 0,
                    observedChange: observedChangeSinceStart,
                    matchedExpectedValueAtStart,
                    completionReason: expectedValue.HasValue ? "ExpectedValueReached" : "ValueChanged");
            }
        }

        var pollCount = 0;
        while (stopwatch.ElapsedMilliseconds < effectiveTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingDelay = effectiveTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
            if (remainingDelay <= 0)
            {
                break;
            }

            var beforePollDelay = BeforePollDelayForTesting;
            if (beforePollDelay is not null)
            {
                await beforePollDelay().ConfigureAwait(false);
            }

            await Task.Delay(Math.Min(effectivePollIntervalMs, remainingDelay), cancellationToken).ConfigureAwait(false);
            pollCount++;

            var currentSnapshot = await ReadSnapshotAsync(
                processId,
                elementId,
                propertyName,
                cancellationToken,
                countAgainstRateLimit: false).ConfigureAwait(false);
            if (currentSnapshot.Error != null)
            {
                return currentSnapshot.Error;
            }

            observedChangeSinceStart |= HasObservedChange(initialSnapshot, currentSnapshot);
            latestSnapshot = currentSnapshot;

            if (HasReachedTarget(initialSnapshot, currentSnapshot, expectedValue, matchedExpectedValueAtStart, observedChangeSinceStart))
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
                    observedChange: observedChangeSinceStart,
                    matchedExpectedValueAtStart,
                    completionReason: expectedValue.HasValue ? "ExpectedValueReached" : "ValueChanged");
            }
        }

        if (stopwatch.ElapsedMilliseconds >= effectiveTimeoutMs && pollCount == 0)
        {
            return BuildWaitResult(
                changed: false,
                timedOut: true,
                propertyName,
                elementId,
                initialSnapshot,
                latestSnapshot,
                stopwatch.ElapsedMilliseconds,
                pollCount,
                observedChange: observedChangeSinceStart,
                matchedExpectedValueAtStart,
                completionReason: "TimedOut");
        }

        var finalSnapshotResult = await TryReadFinalSnapshotAsync(
            processId,
            elementId,
            propertyName,
            cancellationToken).ConfigureAwait(false);
        if (!finalSnapshotResult.Snapshot.HasValue)
        {
            return BuildWaitResult(
                changed: false,
                timedOut: true,
                propertyName,
                elementId,
                initialSnapshot,
                latestSnapshot,
                stopwatch.ElapsedMilliseconds,
                pollCount,
                observedChange: observedChangeSinceStart,
                matchedExpectedValueAtStart,
                completionReason: "TimedOut",
                stateAfterTimeoutUnknown: finalSnapshotResult.TimedOut,
                requiresReconnect: finalSnapshotResult.TimedOut);
        }

        var finalSnapshot = finalSnapshotResult.Snapshot.Value;
        if (finalSnapshot.Error != null)
        {
            return finalSnapshot.Error;
        }

        observedChangeSinceStart |= HasObservedChange(initialSnapshot, finalSnapshot);

        if (HasReachedTarget(initialSnapshot, finalSnapshot, expectedValue, matchedExpectedValueAtStart, observedChangeSinceStart))
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
                observedChange: observedChangeSinceStart,
                matchedExpectedValueAtStart,
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
                observedChange: observedChangeSinceStart,
                matchedExpectedValueAtStart,
            completionReason: "TimedOut");
    }

    private static int GetRemainingBudgetMs(int effectiveTimeoutMs, Stopwatch stopwatch)
    {
        return Math.Max(0, effectiveTimeoutMs - (int)stopwatch.ElapsedMilliseconds);
    }

    private static bool HasReachedTarget(
        DpSnapshot initialSnapshot,
        DpSnapshot currentSnapshot,
        JsonElement? expectedValue,
        bool matchedExpectedValueAtStart,
        bool observedChangeSinceStart)
    {
        if (expectedValue.HasValue)
        {
            return JsonValueMatchesFormatted(expectedValue.Value, currentSnapshot.FormattedValue)
                && (!matchedExpectedValueAtStart || observedChangeSinceStart);
        }

        return HasObservedChange(initialSnapshot, currentSnapshot);
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
}
