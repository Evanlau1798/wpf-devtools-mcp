using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class EventTraceEdgeCaseE2eTests : SharedStateMcpE2eTestBase
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public EventTraceEdgeCaseE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
        : base(fixture)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task TraceRoutedEvents_OnEventStormButtonClick_ShouldCaptureClickEvent()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateEventTraceLabTabAsync();

        var buttonId = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "EventStormButton");
        buttonId.Should().NotBeNull("Golden test app should expose EventStormButton for routed-event trace regression coverage.");

        var start = await _fixture.Client.CallToolAsync(
            "trace_routed_events",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonId,
                eventName = "Click",
                mode = "start",
                duration = 2000,
                allowShortStartDuration = true
            });
        start.GetProperty("success").GetBoolean().Should().BeTrue(start.GetRawText());

        var click = await _fixture.Client.CallToolAsync(
            "click_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonId
            });
        click.GetProperty("success").GetBoolean().Should().BeTrue(click.GetRawText());

        var trace = await E2eTestHelpers.WaitForTraceEventAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            TimeSpan.FromSeconds(2));
        _output.WriteLine(trace.GetRawText());

        trace.GetProperty("success").GetBoolean().Should().BeTrue(trace.GetRawText());
        trace.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0, trace.GetRawText());
    }

    [Fact]
    public async Task TraceRoutedEvents_OnRoutedProbeBorderMouseDown_ShouldCaptureMouseDown()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateEventTraceLabTabAsync();

        var borderId = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "RoutedProbeBorder");
        borderId.Should().NotBeNull("Golden test app should expose RoutedProbeBorder for routed-event trace regression coverage.");

        var start = await _fixture.Client.CallToolAsync(
            "trace_routed_events",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = borderId,
                eventName = "MouseDown",
                mode = "start",
                duration = 2000,
                allowShortStartDuration = true
            });
        start.GetProperty("success").GetBoolean().Should().BeTrue(start.GetRawText());

        var fire = await _fixture.Client.CallToolAsync(
            "fire_routed_event",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = borderId,
                eventName = "MouseDown"
            });
        fire.GetProperty("success").GetBoolean().Should().BeTrue(fire.GetRawText());

        var trace = await E2eTestHelpers.WaitForTraceEventAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            TimeSpan.FromSeconds(2));
        _output.WriteLine(trace.GetRawText());

        trace.GetProperty("success").GetBoolean().Should().BeTrue(trace.GetRawText());
        trace.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0, trace.GetRawText());
    }

    private async Task ActivateEventTraceLabTabAsync()
    {
        var tabId = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "EventTraceLabTab");
        tabId.Should().NotBeNull("Golden test app should expose EventTraceLabTab through get_namescope.");

        var click = await _fixture.Client.CallToolAsync(
            "click_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = tabId
            });
        click.GetProperty("success").GetBoolean().Should().BeTrue(click.GetRawText());
    }
}
