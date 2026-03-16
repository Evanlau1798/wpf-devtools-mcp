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

        var buttonElementId = await E2eTestHelpers.FindElementByTypeAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "Button");
        buttonElementId.Should().NotBeNull("TestApp should contain a button for width watch coverage");

        var watch = await _fixture.Client.CallToolAsync(
            "watch_dp_changes",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyName = "Width"
            });
        watch.GetProperty("success").GetBoolean().Should().BeTrue();

        var mutation = await _fixture.Client.CallToolAsync(
            "set_dp_value",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyName = "Width",
                value = 222
            });

        mutation.GetProperty("success").GetBoolean().Should().BeTrue();
        mutation.GetProperty("pendingEventCount").GetInt32().Should().BeGreaterThan(0);
        mutation.GetProperty("pendingEventsOrigin").GetString().Should().Be("piggybackSharedBuffer");
        mutation.GetProperty("pendingEventsMayIncludePriorContext").GetBoolean().Should().BeTrue();

        var pendingDpEvent = mutation.GetProperty("pendingEvents").EnumerateArray().Single(item =>
            item.GetProperty("eventType").GetString() == "DpChange"
            && item.GetProperty("elementId").GetString() == buttonElementId
            && item.GetProperty("propertyName").GetString() == "Width");
        pendingDpEvent.TryGetProperty("sourceKey", out _).Should().BeFalse();

        var cleanup = await _fixture.Client.CallToolAsync(
            "clear_dp_value",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyName = "Width"
            });
        cleanup.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
