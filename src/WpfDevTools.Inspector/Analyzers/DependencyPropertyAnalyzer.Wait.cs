using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class DependencyPropertyAnalyzer
{
    /// <summary>
    /// Poll a dependency property until it changes or reaches an expected value.
    /// </summary>
    public object WaitForChange(
        string propertyName,
        string? elementId = null,
        int? timeoutMs = null,
        int? pollIntervalMs = null,
        JsonElement? expectedValue = null,
        CancellationToken cancellationToken = default)
    {
        const int defaultTimeoutMs = 5000;
        const int defaultPollIntervalMs = 200;

        var effectiveTimeoutMs = timeoutMs ?? defaultTimeoutMs;
        var effectivePollIntervalMs = pollIntervalMs ?? defaultPollIntervalMs;

        if (effectiveTimeoutMs < 1 || effectiveTimeoutMs > 30000)
        {
            return ToolErrorFactory.InvalidArgument(
                "timeoutMs must be between 1 and 30000.",
                "Provide a bounded timeout in milliseconds. Use shorter waits for UI transitions and longer waits only when necessary.");
        }

        if (effectivePollIntervalMs < 50 || effectivePollIntervalMs > 5000)
        {
            return ToolErrorFactory.InvalidArgument(
                "pollIntervalMs must be between 50 and 5000.",
                "Use a polling interval between 50ms and 5000ms to avoid excessive load or excessively slow detection.");
        }

        var initialSnapshot = ReadDpSnapshot(propertyName, elementId);
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

        var stopwatch = Stopwatch.StartNew();
        var pollCount = 0;
        while (stopwatch.ElapsedMilliseconds < effectiveTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!WaitForNextPoll(stopwatch, effectiveTimeoutMs, effectivePollIntervalMs, cancellationToken))
            {
                break;
            }

            pollCount++;

            var currentSnapshot = ReadDpSnapshot(propertyName, elementId);
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

        var finalSnapshot = ReadDpSnapshot(propertyName, elementId);
        if (finalSnapshot.Error != null)
        {
            return finalSnapshot.Error;
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

    private static bool HasReachedTarget(DpSnapshot initialSnapshot, DpSnapshot currentSnapshot, JsonElement? expectedValue)
    {
        if (expectedValue.HasValue)
        {
            return JsonValueMatchesFormatted(expectedValue.Value, currentSnapshot.FormattedValue);
        }

        return !string.Equals(initialSnapshot.FormattedValue, currentSnapshot.FormattedValue, StringComparison.Ordinal) ||
               !string.Equals(initialSnapshot.BaseValueSource, currentSnapshot.BaseValueSource, StringComparison.Ordinal);
    }

    private static bool HasObservedChange(DpSnapshot initialSnapshot, DpSnapshot currentSnapshot)
    {
        return !string.Equals(initialSnapshot.FormattedValue, currentSnapshot.FormattedValue, StringComparison.Ordinal) ||
               !string.Equals(initialSnapshot.BaseValueSource, currentSnapshot.BaseValueSource, StringComparison.Ordinal);
    }

    private DpSnapshot ReadDpSnapshot(string propertyName, string? elementId)
    {
        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return DpSnapshot.FromError(ToolErrorFactory.ElementNotFound(elementId));
        }

        if (element is not DependencyObject depObj)
        {
            return DpSnapshot.FromError(ToolErrorFactory.InvalidArgument(
                "Element is not a DependencyObject",
                "Choose a WPF DependencyObject target from get_visual_tree or find_elements before waiting for dependency property changes."));
        }

        return InvokeSnapshotRead(depObj, settleBindings: true, () =>
        {
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return DpSnapshot.FromError(ToolErrorFactory.PropertyNotFound(propertyName, depObj.GetType().Name));
            }

            var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
            var hadLocalValue = depObj.ReadLocalValue(dp) != DependencyProperty.UnsetValue;
            return new DpSnapshot(
                FormatResponseValue(depObj.GetValue(dp)),
                DependencyPropertyValueSourceNormalizer.Normalize(valueSource.BaseValueSource, hadLocalValue, valueSource.IsAnimated));
        });
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

    private static bool WaitForNextPoll(
        Stopwatch stopwatch,
        int timeoutMs,
        int pollIntervalMs,
        CancellationToken cancellationToken)
    {
        var remainingMs = timeoutMs - stopwatch.ElapsedMilliseconds;
        if (remainingMs <= 0)
        {
            return false;
        }

        var delayMs = (int)Math.Min(pollIntervalMs, remainingMs);
        if (cancellationToken.WaitHandle.WaitOne(delayMs))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return true;
    }

    private readonly record struct DpSnapshot(string? FormattedValue, string BaseValueSource, object? Error = null)
    {
        public static DpSnapshot FromError(object error) => new(null, string.Empty, error);
    }
}
