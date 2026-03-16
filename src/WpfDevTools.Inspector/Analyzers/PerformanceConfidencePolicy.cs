namespace WpfDevTools.Inspector.Analyzers;

internal static class PerformanceConfidencePolicy
{
    internal const int MinRenderSampleCount = 30;
    internal const int MinRenderMonitoringDurationMs = 1000;
    internal const int MinBindingLeakSamplingDurationMs = 3000;
    internal const int RecommendedRenderMeasurementSamples = 5;

    internal static (string Confidence, string Guidance) EvaluateRenderStats(int sampleCount, bool isWarmedUp)
    {
        if (!isWarmedUp || sampleCount < 10)
        {
            return ("low", $"Collect at least {MinRenderSampleCount} samples (~{MinRenderMonitoringDurationMs}ms) before ranking perf regressions.");
        }

        if (sampleCount < MinRenderSampleCount)
        {
            return ("medium", $"Sample quality is improving; capture at least {MinRenderSampleCount} samples for a stable baseline.");
        }

        return ("high", "Sample size is sufficient for baseline-level render analysis.");
    }

    internal static (string Confidence, string Guidance) EvaluateBindingLeakSampling(int samplingDurationMs, int totalTracked)
    {
        if (samplingDurationMs < MinBindingLeakSamplingDurationMs || totalTracked == 0)
        {
            return ("low", $"Use samplingDurationMs>={MinBindingLeakSamplingDurationMs} and drive real UI interaction before leak conclusions.");
        }

        if (samplingDurationMs < 8000)
        {
            return ("medium", "Sampling window is acceptable for triage; use a longer session for release-blocking decisions.");
        }

        return ("high", "Sampling window is strong for comparative leak diagnostics.");
    }
}
