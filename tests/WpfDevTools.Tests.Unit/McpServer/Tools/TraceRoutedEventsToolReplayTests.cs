using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed partial class TraceRoutedEventsToolReplayTests
{
    [Fact]
    public async Task Execute_GetMode_ShouldMergeMatchingPendingEventReplay_WithoutConsumingDrainReplay()
    {
        const int processId = 43121;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-replay-active","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500),
                SessionId: "trace-replay-active"));
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
                        senderName = "EventStormButton",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Button",
                        timestampUtc = now
                    }
                }
            }));

        var traceTool = new TraceRoutedEventsTool(connected.SessionManager);
        var drainTool = new DrainEventsTool(connected.SessionManager);

        var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get" }),
            CancellationToken.None));
        var drainResult = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, eventTypes = new[] { "RoutedEvent" } }),
            CancellationToken.None));

        traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(1, traceResult.GetRawText());
        traceResult.GetProperty("events")[0].GetProperty("eventName").GetString().Should().Be("Click");
        traceResult.GetProperty("events")[0].GetProperty("sender").GetString().Should().Be("Button");
        traceResult.GetProperty("handlerInvocationCount").GetInt32().Should().Be(0);

        drainResult.GetProperty("success").GetBoolean().Should().BeTrue(drainResult.GetRawText());
        drainResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1, drainResult.GetRawText());
        drainResult.GetProperty("pendingEvents")[0].GetProperty("eventName").GetString().Should().Be("Click");
    }

    [Fact]
    public async Task Execute_GetMode_ShouldIgnoreNonMatchingPendingEventReplay()
    {
        const int processId = 43122;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-replay-mismatch","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"captureWindowTooShort"}}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500),
                SessionId: "trace-replay-mismatch"));
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
                        elementId = "Border_47",
                        eventName = "MouseDown",
                        senderType = "Border",
                        senderName = "RoutedProbeBorder",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Border",
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
        traceResult.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("captureWindowTooShort");
    }

    [Theory]
    [InlineData("captureWindowTooShort")]
    [InlineData("eventNotRaised")]
    public async Task Execute_GetMode_ShouldDropZeroEventDiagnosticsAfterReplayMerge(string reasonCode)
    {
        const int processId = 43132;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                sessionId = "trace-zero-event-diagnostics",
                mode = "get",
                isTracing = true,
                eventCount = 0,
                events = Array.Empty<object>(),
                handlerInvocationCount = 0,
                diagnostics = new
                {
                    reasonCode,
                    activeEventName = "Click",
                    resolvedElementId = "Button_46"
                }
            }));

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500),
                SessionId: "trace-zero-event-diagnostics"));
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
                        senderName = "ReplayDiagnosticCleanupButton",
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
        traceResult.TryGetProperty("diagnostics", out _).Should().BeFalse(traceResult.GetRawText());
    }

    [Fact]
    public async Task Execute_GetMode_ShouldMergeReplayWhenCleanupFailedDiagnosticsArePresent()
    {
        const int processId = 43124;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-cleanup-failed","mode":"get","isTracing":false,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"cleanupFailed","cleanupFailureType":"TimeoutException"}}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500),
                SessionId: "trace-cleanup-failed"));
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
                        senderName = "CleanupFailureButton",
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
        traceResult.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("cleanupFailed");
    }

    [Fact]
    public async Task Execute_GetMode_ShouldKeepCleanupFailedAsPrimaryDiagnosticWhenRequestedEventMismatches()
    {
        const int processId = 43129;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-cleanup-failed-mismatch","mode":"get","isTracing":false,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"cleanupFailed","cleanupFailureType":"TimeoutException","requestedEventName":"MouseDown","requestedEventMismatch":true,"activeEventName":"Click"}}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500),
                SessionId: "trace-cleanup-failed-mismatch"));
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
                        senderName = "CleanupFailureMismatchButton",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Button",
                        timestampUtc = now
                    }
                }
            }));

        var traceTool = new TraceRoutedEventsTool(connected.SessionManager);

        var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get", eventName = "MouseDown" }),
            CancellationToken.None));

        traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(0, traceResult.GetRawText());
        traceResult.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("cleanupFailed");
    }

    [Fact]
    public async Task Execute_GetMode_ShouldNotMergeReplayWhenFilterMismatchDiagnosticsArePresent()
    {
        const int processId = 43125;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-filter-mismatch","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"filterMismatch","requestedEventName":"MouseDown","activeEventName":"Click"}}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500),
                SessionId: "trace-filter-mismatch"));
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
                        senderName = "MismatchReplayButton",
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
        traceResult.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("filterMismatch");
    }
}
