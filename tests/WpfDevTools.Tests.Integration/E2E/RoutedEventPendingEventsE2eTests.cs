using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class RoutedEventPendingEventsE2eTests : SharedStateMcpE2eTestBase
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RoutedEventPendingEventsE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
        : base(fixture)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ClickElement_AfterSuccessfulButtonClick_ShouldExposeRoutedEventViaDrainEvents()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateEventTraceLabTabAsync();

        var buttonId = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "EventStormButton");
        buttonId.Should().NotBeNull();

        var click = await _fixture.Client.CallToolAsync(
            "click_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonId
            });
        click.GetProperty("success").GetBoolean().Should().BeTrue(click.GetRawText());

        var drain = await _fixture.Client.CallToolAsync(
            "drain_events",
            new
            {
                processId = _fixture.TestAppProcessId,
                eventTypes = new[] { "RoutedEvent" },
                elementId = buttonId
            });
        _output.WriteLine(drain.GetRawText());

        drain.GetProperty("success").GetBoolean().Should().BeTrue(drain.GetRawText());
        drain.GetProperty("pendingEventCount").GetInt32().Should().BeGreaterThan(0, drain.GetRawText());
        var routedEvent = drain.GetProperty("pendingEvents").EnumerateArray()
            .First(item =>
                item.GetProperty("eventType").GetString() == "RoutedEvent"
                && item.GetProperty("elementId").GetString() == buttonId
                && item.GetProperty("senderType").GetString() == "Button");
        routedEvent.GetProperty("eventName").GetString().Should().Be("Click");
    }

    [Fact]
    public async Task FireRoutedEvent_AfterSuccessfulMouseDown_ShouldExposeRoutedEventViaDrainEvents()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateEventTraceLabTabAsync();

        var borderId = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "RoutedProbeBorder");
        borderId.Should().NotBeNull();

        var fire = await _fixture.Client.CallToolAsync(
            "fire_routed_event",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = borderId,
                eventName = "MouseDown"
            });
        fire.GetProperty("success").GetBoolean().Should().BeTrue(fire.GetRawText());

        var drain = await _fixture.Client.CallToolAsync(
            "drain_events",
            new
            {
                processId = _fixture.TestAppProcessId,
                eventTypes = new[] { "RoutedEvent" },
                elementId = borderId
            });
        _output.WriteLine(drain.GetRawText());

        drain.GetProperty("success").GetBoolean().Should().BeTrue(drain.GetRawText());
        drain.GetProperty("pendingEventCount").GetInt32().Should().BeGreaterThan(0, drain.GetRawText());
        var routedEvent = drain.GetProperty("pendingEvents").EnumerateArray()
            .First(item =>
                item.GetProperty("eventType").GetString() == "RoutedEvent"
                && item.GetProperty("elementId").GetString() == borderId
                && item.GetProperty("senderType").GetString() == "Border");
        routedEvent.GetProperty("eventName").GetString().Should().Be("MouseDown");
    }

    private async Task ActivateEventTraceLabTabAsync()
    {
        var tabId = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "EventTraceLabTab");
        tabId.Should().NotBeNull();

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
