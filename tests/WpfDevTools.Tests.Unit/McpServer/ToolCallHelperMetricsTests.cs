using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public class ToolCallHelperMetricsTests
{
    [Fact]
    public async Task ExecuteAndWrapAsync_WithMetrics_ShouldRecordSuccessMetrics()
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
            snapshot.ErrorCount.Should().Be(0);
        }
        finally
        {
            ToolCallHelper.ResetCacheForTesting();
        }
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithMetrics_ShouldRecordErrorMetrics()
    {
        var metrics = new MetricsCollector();
        ToolCallHelper.SetMetricsCollector(metrics);
        try
        {
            await ToolCallHelper.ExecuteAndWrapAsync(
                (args, ct) => Task.FromResult<object>(new { success = false, error = "test error" }),
                null,
                CancellationToken.None);

            var snapshot = metrics.GetSnapshot();
            snapshot.TotalRequests.Should().BeGreaterThanOrEqualTo(1);
            snapshot.ErrorCount.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            ToolCallHelper.ResetCacheForTesting();
        }
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithMetrics_WhenExceptionThrown_ShouldRecordAsError()
    {
        var metrics = new MetricsCollector();
        ToolCallHelper.SetMetricsCollector(metrics);
        try
        {
            await ToolCallHelper.ExecuteAndWrapAsync(
                (args, ct) => throw new InvalidOperationException("boom"),
                null,
                CancellationToken.None);

            var snapshot = metrics.GetSnapshot();
            snapshot.TotalRequests.Should().BeGreaterThanOrEqualTo(1);
            snapshot.ErrorCount.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            ToolCallHelper.ResetCacheForTesting();
        }
    }
}