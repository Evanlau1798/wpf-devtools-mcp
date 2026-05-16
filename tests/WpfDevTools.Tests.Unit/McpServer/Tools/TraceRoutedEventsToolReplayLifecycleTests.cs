using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class TraceRoutedEventsToolReplayTests
{
    [Fact]
    public async Task Execute_GetMode_ShouldMergeReplayOnFirstGetWhenResponseRehydratesTraceState()
    {
        const int processId = 43130;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                sessionId = "trace-rehydrate-merge",
                mode = "get",
                isTracing = true,
                eventCount = 0,
                events = Array.Empty<object>(),
                handlerInvocationCount = 0,
                diagnostics = new
                {
                    reasonCode = "captureWindowTooShort",
                    activeEventName = "Click",
                    resolvedElementId = "Button_46"
                },
                activeEventName = "Click",
                resolvedElementId = "Button_46",
                traceStartedAtUtc = now.AddMilliseconds(-400),
                effectiveDurationMs = 1000,
                registrationCount = 1
            }));

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
                        senderName = "RehydratedReplayButton",
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
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(1, traceResult.GetRawText());
    }

    [Fact]
    public async Task Execute_GetMode_ShouldMergeReplayWhenTraceCompletesDuringGetAndStateIsCleared()
    {
        const int processId = 43131;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-finished","mode":"get","isTracing":false,"eventCount":0,"events":[],"handlerInvocationCount":0}""");

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
                        senderName = "CompletedTraceReplayButton",
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
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(1, traceResult.GetRawText());
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().BeNull();
    }

    [Fact]
    public async Task Execute_GetMode_AfterFireRoutedEventMouseDown_ShouldMergeReplayFromRealToolSequence()
    {
        const int processId = 43123;
        var replayEventTimestamp = DateTimeOffset.UtcNow.AddSeconds(1);
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-fire-sequence","mode":"start","eventName":"MouseDown","isTracing":true,"effectiveDuration":2500}""",
            """{"success":true,"message":"Event 'MouseDown' fired successfully","eventName":"MouseDown"}""",
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "RoutedEvent",
                        elementId = "Border_47",
                        eventName = "MouseDown",
                        senderType = "Border",
                        senderName = "RoutedProbeBorder",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Border",
                        timestampUtc = replayEventTimestamp
                    }
                }
            }),
            """{"success":true,"sessionId":"trace-fire-sequence","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""");

        var traceTool = new TraceRoutedEventsTool(connected.SessionManager);
        var fireTool = new FireRoutedEventTool(connected.SessionManager);
        var drainTool = new DrainEventsTool(connected.SessionManager);

        var startResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                elementId = "Border_47",
                eventName = "MouseDown",
                mode = "start",
                duration = 2500,
                allowShortStartDuration = true
            }),
            CancellationToken.None));
        startResult.GetProperty("success").GetBoolean().Should().BeTrue(startResult.GetRawText());

        var fireResult = JsonSerializer.SerializeToElement(await fireTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                elementId = "Border_47",
                eventName = "MouseDown"
            }),
            CancellationToken.None));
        fireResult.GetProperty("success").GetBoolean().Should().BeTrue(fireResult.GetRawText());
        fireResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1, fireResult.GetRawText());

        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.EventName.Should().Be("MouseDown");
        state.ActiveTrace.ElementId.Should().Be("Border_47");

        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayPayload).Should().BeTrue();
        replayPayload.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        replayPayload.GetProperty("pendingEvents")[0].GetProperty("eventName").GetString().Should().Be("MouseDown");
        replayPayload.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Border_47");

        var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get" }),
            CancellationToken.None));
        var drainResult = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, eventTypes = new[] { "RoutedEvent" } }),
            CancellationToken.None));

        traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(1, traceResult.GetRawText());
        traceResult.GetProperty("events")[0].GetProperty("eventName").GetString().Should().Be("MouseDown");
        traceResult.GetProperty("events")[0].GetProperty("sender").GetString().Should().Be("Border");

        drainResult.GetProperty("success").GetBoolean().Should().BeTrue(drainResult.GetRawText());
        drainResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1, drainResult.GetRawText());
        drainResult.GetProperty("pendingEvents")[0].GetProperty("eventName").GetString().Should().Be("MouseDown");
    }
}
