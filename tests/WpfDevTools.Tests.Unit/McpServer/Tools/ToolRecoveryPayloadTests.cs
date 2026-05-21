using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class ToolRecoveryPayloadTests
{
    [Fact]
    public void CreateStepFailure_WhenInnerPayloadHasRateLimitBackoff_ShouldPreserveRecoveryFields()
    {
        var inner = JsonSerializer.SerializeToElement(new
        {
            success = false,
            error = "Rate limit exceeded for process 123.",
            errorCode = "RateLimitExceeded",
            availableTokens = 0,
            retryAfterSeconds = 7,
            retryAfter = "Wait 7 seconds for rate limit to reset"
        });

        var payload = ToolRecoveryPayload.CreateStepFailure(
            "capture_state_snapshot failed while reading get_dp_value_source.",
            "Slow down and retry the snapshot.",
            inner);

        payload.ErrorCode.Should().Be("RateLimitExceeded");
        payload.Recovery.Should().NotBeNull();
        payload.Recovery!.AvailableTokens.Should().Be(0);
        payload.Recovery.RetryAfterSeconds.Should().Be(7);
        payload.Recovery.RetryAfter.Should().Be("Wait 7 seconds for rate limit to reset");
        var json = JsonSerializer.SerializeToElement(payload);
        json.GetProperty("availableTokens").GetInt32().Should().Be(0);
        json.GetProperty("retryAfterSeconds").GetInt32().Should().Be(7);
        json.GetProperty("retryAfter").GetString().Should().Be("Wait 7 seconds for rate limit to reset");
    }

    [Fact]
    public void CreateStepFailure_WhenInnerRecoveryHasAvailableEvents_ShouldPreserveRecoveryOptions()
    {
        var inner = JsonSerializer.SerializeToElement(new
        {
            success = false,
            error = "Unknown event.",
            errorCode = "InvalidArgument",
            recovery = new
            {
                hint = "Pick a supported routed event.",
                availableEvents = new[] { "Click", "MouseDown" }
            }
        });

        var payload = ToolRecoveryPayload.CreateStepFailure(
            "trace_routed_events failed.",
            "Pick another event.",
            inner);

        payload.Recovery.Should().NotBeNull();
        payload.Recovery!.AvailableEvents.Should().Equal("Click", "MouseDown");
        var json = JsonSerializer.SerializeToElement(payload);
        json.GetProperty("availableEvents").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("Click", "MouseDown");
    }
}
