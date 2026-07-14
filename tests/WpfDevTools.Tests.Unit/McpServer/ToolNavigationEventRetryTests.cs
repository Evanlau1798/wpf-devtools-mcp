using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class ToolNavigationEventRetryTests
{
    [Fact]
    public async Task TraceRoutedEventsWrapper_ShouldUsePublicToolName()
    {
        var metrics = new MetricsCollector();
        using var scope = ToolCallHelper.BeginTestScope(metricsCollector: metrics);

        _ = await EventMcpTools.TraceRoutedEvents(
            new SessionManager(),
            processId: 12345,
            eventName: "Click",
            durationMs: 5000,
            mode: "start",
            cancellationToken: CancellationToken.None);

        metrics.GetSnapshot().MethodMetrics.Keys.Should().ContainSingle()
            .Which.Should().Be("trace_routed_events");
    }

    [Fact]
    public void Plan_WhenShortStartDurationIsExpanded_ShouldRecommendExactOptInRetry()
    {
        var planner = new ToolNavigationPlanner(new ToolNavigationRegistry());
        var payload = JsonSerializer.SerializeToElement(new
        {
            success = true,
            mode = "start",
            requestedDuration = 5000,
            effectiveDuration = 30000,
            shortDurationOverrideUsed = false
        });
        var arguments = ToolCallHelper.BuildJsonArgs(
            ("processId", 12345),
            ("eventName", "Click"),
            ("elementId", "SaveButton"),
            ("duration", 5000),
            ("mode", "start"),
            ("allowShortStartDuration", false),
            ("maxEvents", 20));

        var steps = planner.Plan("trace_routed_events", payload, arguments);

        steps.Should().ContainSingle();
        var retry = steps[0];
        retry.Tool.Should().Be("trace_routed_events");
        retry.Params.GetProperty("processId").GetInt32().Should().Be(12345);
        retry.Params.GetProperty("eventName").GetString().Should().Be("Click");
        retry.Params.GetProperty("elementId").GetString().Should().Be("SaveButton");
        retry.Params.GetProperty("durationMs").GetInt32().Should().Be(5000);
        retry.Params.GetProperty("mode").GetString().Should().Be("start");
        retry.Params.GetProperty("allowShortStartDuration").GetBoolean().Should().BeTrue();
        retry.Params.GetProperty("maxEvents").GetInt32().Should().Be(20);
        retry.ExpectedOutcome.Should().Contain("5000");
    }

    [Theory]
    [InlineData(5000, 5000, true)]
    [InlineData(30000, 30000, false)]
    [InlineData(5000, 3000, false)]
    public void Plan_WhenStartDurationWasNotRaised_ShouldNotRecommendRetry(
        int requestedDuration,
        int effectiveDuration,
        bool shortDurationOverrideUsed)
    {
        var planner = new ToolNavigationPlanner(new ToolNavigationRegistry());
        var payload = JsonSerializer.SerializeToElement(new
        {
            success = true,
            mode = "start",
            requestedDuration,
            effectiveDuration,
            shortDurationOverrideUsed
        });
        var arguments = ToolCallHelper.BuildJsonArgs(
            ("eventName", "Click"),
            ("duration", requestedDuration),
            ("mode", "start"),
            ("allowShortStartDuration", shortDurationOverrideUsed));

        var steps = planner.Plan("trace_routed_events", payload, arguments);

        steps.Should().BeEmpty();
    }
}
