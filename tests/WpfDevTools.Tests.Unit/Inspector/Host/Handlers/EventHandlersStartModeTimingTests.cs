using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Host.Handlers;

public class EventHandlersStartModeTimingTests
{
    [Fact]
    public void StartModeMinDuration_ShouldBe30Seconds()
    {
        InspectorConstants.Defaults.StartModeMinDuration.Should().Be(30000,
            "Start mode minimum duration should be 30 seconds for AI agent round-trips");
    }

    [Fact]
    public void StartModeMinDuration_ShouldBeGreaterThanDefaultEventTraceDuration()
    {
        InspectorConstants.Defaults.StartModeMinDuration.Should()
            .BeGreaterThan(InspectorConstants.Defaults.EventTraceDuration,
                "Start mode minimum must exceed default trace duration to prevent premature auto-stop");
    }

    [StaFact]
    public async Task StartMode_WithShortDuration_ShouldShowBothRequestedAndEffectiveDuration()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);

        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var requestedDuration = 1500;
        var @params = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = requestedDuration
        });

        // Act
        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);
        var payload = JsonSerializer.SerializeToElement(result);

        // Assert
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("requestedDuration").GetInt32().Should().Be(requestedDuration,
            "Response should transparently show the original requested duration");
        payload.GetProperty("effectiveDuration").GetInt32().Should().Be(
            InspectorConstants.Defaults.StartModeMinDuration,
            "Response should show the enforced effective duration");
    }

    [StaFact]
    public async Task StartMode_WithLongDuration_ShouldNotOverride()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);

        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var requestedDuration = 45000; // longer than StartModeMinDuration
        var @params = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = requestedDuration
        });

        // Act
        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);
        var payload = JsonSerializer.SerializeToElement(result);

        // Assert
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("requestedDuration").GetInt32().Should().Be(requestedDuration);
        payload.GetProperty("effectiveDuration").GetInt32().Should().Be(requestedDuration,
            "When requested duration exceeds minimum, effective should equal requested");
    }

    [StaFact]
    public async Task StartMode_WithShortDurationOverride_ShouldHonorRequestedDuration()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);

        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var requestedDuration = 1200;
        var @params = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = requestedDuration,
            allowShortStartDuration = true
        });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);
        var payload = JsonSerializer.SerializeToElement(result);

        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("requestedDuration").GetInt32().Should().Be(requestedDuration);
        payload.GetProperty("effectiveDuration").GetInt32().Should().Be(requestedDuration);
        payload.GetProperty("shortDurationOverrideUsed").GetBoolean().Should().BeTrue();
    }
}
