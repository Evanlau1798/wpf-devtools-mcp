using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class MetricsCollectorTests
{
    [Fact]
    public void RecordRequest_ShouldIncrementRequestCount()
    {
        // Arrange
        var metrics = new MetricsCollector();

        // Act
        metrics.RecordRequest("get_visual_tree", 100, true);
        metrics.RecordRequest("get_bindings", 50, true);

        // Assert
        var snapshot = metrics.GetSnapshot();
        snapshot.TotalRequests.Should().Be(2);
    }

    [Fact]
    public void RecordRequest_ShouldTrackSuccessAndErrorRates()
    {
        // Arrange
        var metrics = new MetricsCollector();

        // Act
        metrics.RecordRequest("method1", 100, true);
        metrics.RecordRequest("method2", 50, true);
        metrics.RecordRequest("method3", 75, false); // error

        // Assert
        var snapshot = metrics.GetSnapshot();
        snapshot.TotalRequests.Should().Be(3);
        snapshot.SuccessCount.Should().Be(2);
        snapshot.ErrorCount.Should().Be(1);
        snapshot.ErrorRate.Should().BeApproximately(0.333, 0.01);
    }

    [Fact]
    public void RecordRequest_ShouldCalculateLatencyPercentiles()
    {
        // Arrange
        var metrics = new MetricsCollector();

        // Act - record requests with known latencies
        for (int i = 1; i <= 100; i++)
        {
            metrics.RecordRequest("test", i, true);
        }

        // Assert
        var snapshot = metrics.GetSnapshot();
        snapshot.P50Latency.Should().BeApproximately(50, 5);
        snapshot.P95Latency.Should().BeApproximately(95, 5);
        snapshot.P99Latency.Should().BeApproximately(99, 5);
    }

    [Fact]
    public void RecordRequest_ShouldTrackAverageLatency()
    {
        // Arrange
        var metrics = new MetricsCollector();

        // Act
        metrics.RecordRequest("method1", 100, true);
        metrics.RecordRequest("method2", 200, true);
        metrics.RecordRequest("method3", 300, true);

        // Assert
        var snapshot = metrics.GetSnapshot();
        snapshot.AverageLatency.Should().BeApproximately(200, 1);
    }

    [Fact]
    public void RecordRequest_WithPayloadPressure_ShouldTrackSizeAndTruncationMetrics()
    {
        // Arrange
        var metrics = new MetricsCollector();

        // Act
        metrics.RecordRequest(
            "get_bindings",
            latencyMs: 25,
            success: true,
            payloadByteLength: 4096,
            truncated: true);

        // Assert
        var snapshot = metrics.GetSnapshot();
        snapshot.TotalPayloadBytes.Should().Be(4096);
        snapshot.MaxPayloadBytes.Should().Be(4096);
        snapshot.TruncatedPayloadCount.Should().Be(1);
        snapshot.PayloadPressureRate.Should().Be(1);

        var method = snapshot.MethodMetrics["get_bindings"];
        method.TotalPayloadBytes.Should().Be(4096);
        method.MaxPayloadBytes.Should().Be(4096);
        method.TruncatedPayloadCount.Should().Be(1);
    }

    [Fact]
    public void Reset_ShouldClearAllMetrics()
    {
        // Arrange
        var metrics = new MetricsCollector();
        metrics.RecordRequest("method1", 100, true);
        metrics.RecordRequest("method2", 50, false);

        // Act
        metrics.Reset();

        // Assert
        var snapshot = metrics.GetSnapshot();
        snapshot.TotalRequests.Should().Be(0);
        snapshot.SuccessCount.Should().Be(0);
        snapshot.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void GetSnapshot_ShouldReturnImmutableCopy()
    {
        // Arrange
        var metrics = new MetricsCollector();
        metrics.RecordRequest("method1", 100, true);

        // Act
        var snapshot1 = metrics.GetSnapshot();
        metrics.RecordRequest("method2", 50, true);
        var snapshot2 = metrics.GetSnapshot();

        // Assert - snapshot1 should not change
        snapshot1.TotalRequests.Should().Be(1);
        snapshot2.TotalRequests.Should().Be(2);
    }
}
