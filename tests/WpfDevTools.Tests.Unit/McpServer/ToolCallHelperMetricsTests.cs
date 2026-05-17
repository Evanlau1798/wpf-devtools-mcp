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
    public async Task ExecuteAndWrapAsync_WithMetrics_ShouldRecordPayloadSizeAndTruncationPressure()
    {
        var metrics = new MetricsCollector();
        using var toolCallHelperScope = ToolCallHelper.BeginTestScope(metricsCollector: metrics);

        await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new
            {
                success = true,
                truncated = true,
                truncationMetadata = new
                {
                    reasons = new[] { "ResultLimit" }
                },
                payload = new string('x', 256)
            }),
            null,
            CancellationToken.None);

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalPayloadBytes.Should().BeGreaterThan(0);
        snapshot.TruncatedPayloadCount.Should().Be(1);
        snapshot.PayloadPressureRate.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithLargePayloadMetrics_ShouldRecordBoundedPayloadEstimate()
    {
        var metrics = new MetricsCollector();
        using var toolCallHelperScope = ToolCallHelper.BeginTestScope(metricsCollector: metrics);

        await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new
            {
                success = true,
                payload = new string('x', 1024 * 1024)
            }),
            null,
            CancellationToken.None,
            toolName: nameof(ExecuteAndWrapAsync_WithLargePayloadMetrics_ShouldRecordBoundedPayloadEstimate));

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalPayloadBytes.Should().BeGreaterThan(0);
        snapshot.TotalPayloadBytes.Should().BeLessThan(
            64 * 1024,
            "metrics should keep payload size observability bounded for large structured payloads");
    }

    [Fact]
    public void EstimatePayloadBytesForMetrics_WithLargePayload_ShouldUseBoundedAllocation()
    {
        var json = $$"""
            {"success":true,"payload":"{{new string('x', 1024 * 1024)}}"}
            """;
        using var document = System.Text.Json.JsonDocument.Parse(json);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var byteEstimate = ToolCallHelper.EstimatePayloadBytesForMetrics(document.RootElement);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        byteEstimate.Should().BePositive();
        byteEstimate.Should().BeLessThan(64 * 1024);
        allocatedBytes.Should().BeLessThan(
            64 * 1024,
            "metrics payload sizing should use a bounded estimate instead of materializing a raw JSON string copy");
    }

    [Fact]
    public void EstimatePayloadBytesForMetrics_WithLargePropertyName_ShouldUseBoundedAllocation()
    {
        var json = $$"""
            {"{{new string('x', 1024 * 1024)}}":true}
            """;
        using var document = System.Text.Json.JsonDocument.Parse(json);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var byteEstimate = ToolCallHelper.EstimatePayloadBytesForMetrics(document.RootElement);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        byteEstimate.Should().BePositive();
        byteEstimate.Should().BeLessThan(64 * 1024);
        allocatedBytes.Should().BeLessThan(
            64 * 1024,
            "metrics payload sizing should not materialize oversized JSON property names");
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
