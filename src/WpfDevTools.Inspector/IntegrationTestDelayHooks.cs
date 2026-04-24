using System.Threading.Tasks;

namespace WpfDevTools.Inspector;

internal static class IntegrationTestDelayHooks
{
    internal const string DelayBeforeHostStartEnvVar = "WPFDEVTOOLS_TEST_DELAY_BEFORE_HOST_START_MS";
    internal const string DelayAfterPipeConnectEnvVar = "WPFDEVTOOLS_TEST_DELAY_AFTER_PIPE_CONNECT_MS";

    internal static void DelayBeforeHostStartIfConfigured()
    {
#if DEBUG
        var delay = GetDelay(DelayBeforeHostStartEnvVar);
        if (delay > TimeSpan.Zero)
        {
            Thread.Sleep(delay);
        }
#endif
    }

    internal static ValueTask DelayAfterPipeConnectIfConfiguredAsync(CancellationToken cancellationToken)
    {
#if DEBUG
        var delay = GetDelay(DelayAfterPipeConnectEnvVar);
        return delay <= TimeSpan.Zero
            ? ValueTask.CompletedTask
            : new ValueTask(Task.Delay(delay, cancellationToken));
#else
        return ValueTask.CompletedTask;
#endif
    }

#if DEBUG
    private static TimeSpan GetDelay(string environmentVariableName)
    {
        var rawValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!int.TryParse(rawValue, out var delayMs) || delayMs <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(delayMs);
    }
#endif
}