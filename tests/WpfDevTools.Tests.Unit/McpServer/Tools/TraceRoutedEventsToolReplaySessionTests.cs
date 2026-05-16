using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class TraceRoutedEventsToolReplayTests
{
    [Fact]
    public async Task Execute_GetMode_ShouldNotMergeReplayForStaleSessionResponse()
    {
        const int processId = 43127;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-old","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500),
                SessionId: "trace-new"));
        connected.SessionManager.SavePendingEventReplay(
            processId,
            JsonSerializer.SerializeToElement(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "RoutedEvent",
                        elementId = "Button_46",
                        eventName = "Click",
                        senderType = "Button",
                        senderName = "WrongSessionButton",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Button",
                        timestampUtc = now
                    }
                }
            }));

        var traceTool = new TraceRoutedEventsTool(connected.SessionManager);

        var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get" }),
            CancellationToken.None));

        traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(0, traceResult.GetRawText());
    }

    [Fact]
    public async Task Execute_GetMode_ShouldNotMergeReplayFromPreSyncSnapshotAfterAnotherRequestAlreadyEndedSession()
    {
        const int processId = 43133;
        var now = DateTimeOffset.UtcNow;
        ConnectedTraceReplaySession? connected = null;
        connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            ["""{"success":true,"sessionId":"trace-finished","mode":"get","isTracing":false,"eventCount":0,"events":[],"handlerInvocationCount":0}"""],
            _ => connected!.SessionManager.ClearActiveTraceState(processId, "trace-finished"));
        using (connected)
        {
            connected.SessionManager.SetActiveTraceState(
                processId,
                new ActiveTraceNavigationState(
                    "Click",
                    "Button_46",
                    now.AddMilliseconds(-200),
                    TimeSpan.FromMilliseconds(2500),
                    SessionId: "trace-finished"));
            connected.SessionManager.SavePendingEventReplay(
                processId,
                JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    pendingEventCount = 1,
                    droppedEventCount = 0,
                    pendingEvents = new[]
                    {
                        new
                        {
                            eventType = "RoutedEvent",
                            elementId = "Button_46",
                            eventName = "Click",
                            senderType = "Button",
                            senderName = "LateEndedSessionReplayButton",
                            routingStrategy = "Bubble",
                            handled = false,
                            originalSourceType = "Button",
                            timestampUtc = now
                        }
                    }
                }));

            var traceTool = new TraceRoutedEventsTool(connected.SessionManager);

            var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
                ToJsonElement(new { processId, mode = "get" }),
                CancellationToken.None));

            traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
            traceResult.GetProperty("eventCount").GetInt32().Should().Be(0, traceResult.GetRawText());
        }
    }

}
