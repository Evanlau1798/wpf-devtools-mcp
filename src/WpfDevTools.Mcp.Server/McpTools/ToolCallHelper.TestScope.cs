using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    /// <summary>
    /// Set the MetricsCollector instance for recording tool execution metrics.
    /// Called once during DI initialization from Program.cs.
    /// </summary>
    internal static void SetMetricsCollector(MetricsCollector metrics)
    {
        if (ToolCacheOverride.Value is not null)
        {
            MetricsCollectorOverride.Value = metrics;
            return;
        }

        _metrics = metrics;
    }

    /// <summary>
    /// Begin a test-local ToolCallHelper scope that isolates cache entries and optional overrides.
    /// </summary>
    internal static IDisposable BeginTestScope(
        ToolNavigationPlanner? navigationPlanner = null,
        MetricsCollector? metricsCollector = null)
    {
        var previousToolCache = ToolCacheOverride.Value;
        var previousMetricsCollector = MetricsCollectorOverride.Value;
        var previousNavigationPlanner = NavigationPlannerOverride.Value;

        ToolCacheOverride.Value = new ConcurrentDictionary<string, object>();
        MetricsCollectorOverride.Value = metricsCollector ?? previousMetricsCollector;
        NavigationPlannerOverride.Value = navigationPlanner ?? previousNavigationPlanner;

        return new TestScopeRestorer(
            previousToolCache,
            previousMetricsCollector,
            previousNavigationPlanner);
    }

    /// <summary>
    /// Clear the tool cache and metrics. Only for use in tests to ensure test isolation.
    /// </summary>
    internal static void ResetCacheForTesting()
    {
        if (ToolCacheOverride.Value is not null)
        {
            ToolCacheOverride.Value = new ConcurrentDictionary<string, object>();
            return;
        }

        GlobalToolCache.Clear();
        HostToolCaches = new ConditionalWeakTable<SessionManager, ConcurrentDictionary<string, object>>();
        _metrics = null;
        MetricsCollectorOverride.Value = null;
        NavigationPlannerOverride.Value = null;
    }

    internal static void SetNavigationPlannerForTesting(ToolNavigationPlanner planner) =>
        NavigationPlannerOverride.Value = planner ?? throw new ArgumentNullException(nameof(planner));

    private sealed class TestScopeRestorer(
        ConcurrentDictionary<string, object>? previousToolCache,
        MetricsCollector? previousMetricsCollector,
        ToolNavigationPlanner? previousNavigationPlanner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ToolCacheOverride.Value = previousToolCache;
            MetricsCollectorOverride.Value = previousMetricsCollector;
            NavigationPlannerOverride.Value = previousNavigationPlanner;
            _disposed = true;
        }
    }
}
