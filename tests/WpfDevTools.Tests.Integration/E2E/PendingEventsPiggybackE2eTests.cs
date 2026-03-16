using System.Linq;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class PendingEventsPiggybackE2eTests
{
    private readonly McpE2eFixture _fixture;

    public PendingEventsPiggybackE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SetDpValue_WithPiggybackedPendingEvents_ShouldAnnotateSharedBufferCarryover()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateBasicControlsTabAsync();

        var textBoxElementId = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "NameTextBox");
        textBoxElementId.Should().NotBeNull("TestApp should expose NameTextBox through the root namescope");

        var watch = await _fixture.Client.CallToolAsync(
                "watch_dp_changes",
                new
                {
                    processId = _fixture.TestAppProcessId,
                elementId = textBoxElementId,
                propertyName = "Text"
            });
        watch.GetProperty("success").GetBoolean().Should().BeTrue();

        var mutation = await _fixture.Client.CallToolAsync(
                "set_dp_value",
                new
                {
                    processId = _fixture.TestAppProcessId,
                elementId = textBoxElementId,
                propertyName = "Text",
                value = "piggyback-e2e"
            });

        mutation.GetProperty("success").GetBoolean().Should().BeTrue();
        mutation.GetProperty("pendingEventCount").GetInt32().Should().BeGreaterThan(0);
        mutation.GetProperty("pendingEventsOrigin").GetString().Should().Be("piggybackSharedBuffer");
        mutation.GetProperty("pendingEventsMayIncludePriorContext").GetBoolean().Should().BeTrue();

        var pendingDpEvent = mutation.GetProperty("pendingEvents").EnumerateArray().Single(item =>
            item.GetProperty("eventType").GetString() == "DpChange"
            && item.GetProperty("elementId").GetString() == textBoxElementId
            && item.GetProperty("propertyName").GetString() == "Text");
        pendingDpEvent.TryGetProperty("sourceKey", out _).Should().BeFalse();

        var cleanup = await _fixture.Client.CallToolAsync(
                "clear_dp_value",
                new
                {
                    processId = _fixture.TestAppProcessId,
                elementId = textBoxElementId,
                propertyName = "Text"
            });
        cleanup.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    private async Task ActivateBasicControlsTabAsync()
    {
        var tabId = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "BasicControlsTab");
        tabId.Should().NotBeNull("TestApp should expose BasicControlsTab through the root namescope");

        var clickResult = await _fixture.Client.CallToolAsync(
            "click_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = tabId,
                navigation = false
            });

        clickResult.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
