namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class PerformanceAnalyzer
{
    private const int DefaultRenderWarmUpWaitMs = 250;

    private static void WaitForRenderWarmUp(bool warmUp)
    {
        var waitStartUtc = DateTime.UtcNow;
        var maxWait = TimeSpan.FromSeconds(10);

        while (true)
        {
            bool completed;
            lock (_lock)
            {
                var monitoringElapsedMs = (DateTime.UtcNow - _monitoringStartTime).TotalMilliseconds;
                completed = warmUp
                    ? _frameTimes.Count >= PerformanceConfidencePolicy.MinRenderSampleCount
                      || monitoringElapsedMs >= PerformanceConfidencePolicy.MinRenderMonitoringDurationMs
                    : _frameTimes.Count > 0
                      || monitoringElapsedMs >= DefaultRenderWarmUpWaitMs;
            }

            if (completed || (DateTime.UtcNow - waitStartUtc) > maxWait)
            {
                return;
            }

            Thread.Sleep(20);
        }
    }

    private static int GetEffectiveBindingLeakSamplingDuration(int? samplingDurationMs, bool warmUp)
    {
        var requestedSamplingDurationMs = Math.Max(0, samplingDurationMs ?? 0);
        if (!warmUp)
        {
            return requestedSamplingDurationMs;
        }

        return Math.Max(
            requestedSamplingDurationMs,
            PerformanceConfidencePolicy.MinBindingLeakSamplingDurationMs);
    }
}
