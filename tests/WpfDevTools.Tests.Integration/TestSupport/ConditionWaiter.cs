using System.Diagnostics;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static class ConditionWaiter
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

    public static async Task<T> WaitForAsync<T>(
        Func<Task<T>> action,
        Func<T, bool> condition,
        TimeSpan timeout,
        string failureMessage,
        TimeSpan? pollInterval = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var interval = pollInterval ?? DefaultPollInterval;
        var lastResult = await action().ConfigureAwait(false);

        while (!condition(lastResult))
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException($"{failureMessage} Last observed result: {FormatResult(lastResult)}");
            }

            await Task.Delay(interval).ConfigureAwait(false);
            lastResult = await action().ConfigureAwait(false);
        }

        return lastResult;
    }

    public static async Task<T> WaitForAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Func<T, bool> condition,
        TimeSpan timeout,
        string failureMessage,
        TimeSpan? pollInterval = null)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        var interval = pollInterval ?? DefaultPollInterval;
        T? lastResult = default;
        var hasResult = false;

        try
        {
            lastResult = await action(timeoutSource.Token).ConfigureAwait(false);
            timeoutSource.Token.ThrowIfCancellationRequested();
            hasResult = true;

            while (!condition(lastResult))
            {
                await Task.Delay(interval, timeoutSource.Token).ConfigureAwait(false);
                lastResult = await action(timeoutSource.Token).ConfigureAwait(false);
                timeoutSource.Token.ThrowIfCancellationRequested();
            }

            return lastResult;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            var lastObserved = hasResult ? $" Last observed result: {FormatResult(lastResult)}" : string.Empty;
            throw new TimeoutException(failureMessage + lastObserved);
        }
    }

    public static void WaitUntil(
        Func<bool> condition,
        TimeSpan timeout,
        string failureMessage,
        TimeSpan? pollInterval = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(10);

        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(failureMessage);
            }

            Thread.Sleep(interval);
        }
    }

    private static string FormatResult<T>(T result)
    {
        if (result is null)
        {
            return "<null>";
        }

        if (result is JsonElement jsonElement)
        {
            return jsonElement.GetRawText();
        }

        try
        {
            return JsonSerializer.Serialize(result);
        }
        catch
        {
            return result.ToString() ?? result.GetType().FullName ?? typeof(T).FullName ?? "<unknown>";
        }
    }
}
