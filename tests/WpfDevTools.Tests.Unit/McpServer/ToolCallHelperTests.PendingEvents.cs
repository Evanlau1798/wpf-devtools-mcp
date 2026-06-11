using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class ToolCallHelperTests_PendingEventsContract : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenPendingEventsAreEmpty_ShouldOmitPendingEventsButKeepCounters()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 3,
                pendingEvents = Array.Empty<object>()
            }),
            null,
            CancellationToken.None,
            toolName: "known_tool");

        var payload = result.StructuredContent!.Value;
        payload.TryGetProperty("pendingEvents", out _).Should().BeFalse();
        payload.GetProperty("pendingEventCount").GetInt32().Should().Be(0);
        payload.GetProperty("droppedEventCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenPendingEventsExist_ShouldPreserveEventsAndCounters()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Button_1",
                        propertyName = "Width"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "known_tool");

        var payload = result.StructuredContent!.Value;
        payload.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        payload.GetProperty("droppedEventCount").GetInt32().Should().Be(0);
        payload.GetProperty("pendingEvents")[0].GetProperty("eventType").GetString().Should().Be("DpChange");
    }
}
