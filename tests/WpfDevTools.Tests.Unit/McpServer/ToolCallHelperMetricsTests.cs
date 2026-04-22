using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public class ToolCallHelperMetricsTests
{
    [Fact]
    public async Task ExecuteAndWrapAsync_WithoutTestScope_ShouldUseProcessWideMetricsCollector()
    {
        var metrics = new MetricsCollector();
        ToolCallHelper.SetMetricsCollector(metrics);
        try
        {
            await ToolCallHelper.ExecuteAndWrapAsync(
                (args, ct) => Task.FromResult<object>(new { success = true }),
                null,
                CancellationToken.None);

            var snapshot = metrics.GetSnapshot();
            snapshot.TotalRequests.Should().BeGreaterThanOrEqualTo(1);
            snapshot.SuccessCount.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            ToolCallHelper.ResetCacheForTesting();
        }
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithMetrics_ShouldRecordSuccessMetrics()
    {
        var metrics = new MetricsCollector();
        using var toolCallHelperScope = ToolCallHelper.BeginTestScope(metricsCollector: metrics);

        await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true }),
            null,
            CancellationToken.None);

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalRequests.Should().BeGreaterThanOrEqualTo(1);
        snapshot.SuccessCount.Should().BeGreaterThanOrEqualTo(1);
        snapshot.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithMetrics_ShouldRecordErrorMetrics()
    {
        var metrics = new MetricsCollector();
        using var toolCallHelperScope = ToolCallHelper.BeginTestScope(metricsCollector: metrics);

        await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = false, error = "test error" }),
            null,
            CancellationToken.None);

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalRequests.Should().BeGreaterThanOrEqualTo(1);
        snapshot.ErrorCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithMetrics_WhenExceptionThrown_ShouldRecordAsError()
    {
        var metrics = new MetricsCollector();
        using var toolCallHelperScope = ToolCallHelper.BeginTestScope(metricsCollector: metrics);

        await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => throw new InvalidOperationException("boom"),
            null,
            CancellationToken.None);

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalRequests.Should().BeGreaterThanOrEqualTo(1);
        snapshot.ErrorCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenNestedMetricScopeDisposes_ShouldRestoreOuterCollector()
    {
        var outerMetrics = new MetricsCollector();
        using var outerScope = ToolCallHelper.BeginTestScope(metricsCollector: outerMetrics);

        var innerMetrics = new MetricsCollector();
        using (ToolCallHelper.BeginTestScope(metricsCollector: innerMetrics))
        {
            await ToolCallHelper.ExecuteAndWrapAsync(
                (args, ct) => Task.FromResult<object>(new { success = true }),
                null,
                CancellationToken.None);
        }

        innerMetrics.GetSnapshot().TotalRequests.Should().BeGreaterThanOrEqualTo(1);

        await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true }),
            null,
            CancellationToken.None);

        outerMetrics.GetSnapshot().TotalRequests.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenScopedMetricsScopeEnds_ShouldResumeProcessWideCollector()
    {
        var globalMetrics = new MetricsCollector();
        ToolCallHelper.SetMetricsCollector(globalMetrics);
        try
        {
            var scopedMetrics = new MetricsCollector();
            using (ToolCallHelper.BeginTestScope(metricsCollector: scopedMetrics))
            {
                await ToolCallHelper.ExecuteAndWrapAsync(
                    (args, ct) => Task.FromResult<object>(new { success = true }),
                    null,
                    CancellationToken.None);
            }

            scopedMetrics.GetSnapshot().TotalRequests.Should().BeGreaterThanOrEqualTo(1);
            globalMetrics.GetSnapshot().TotalRequests.Should().Be(0);

            await ToolCallHelper.ExecuteAndWrapAsync(
                (args, ct) => Task.FromResult<object>(new { success = true }),
                null,
                CancellationToken.None);

            globalMetrics.GetSnapshot().TotalRequests.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            ToolCallHelper.ResetCacheForTesting();
        }
    }
}
