using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class TraceRoutedEventsToolReplayTests
{
    [Fact]
    public async Task Execute_GetMode_ShouldNotMergeReplayWhenEventTimestampFallsOutsideTraceWindow()
    {
        const int processId = 43126;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"sessionId":"trace-window","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-500),
                TimeSpan.FromMilliseconds(250),
                SessionId: "trace-window"));
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
                        senderName = "LateButton",
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
    public async Task Execute_GetMode_ShouldNotMergeReplayWhenEventTimestampIsTooEarlyEvenIfSavedAtIsInsideFallbackWindow()
    {
        const int processId = 43136;
        var currentTime = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            new[]
            {
                """{"success":true,"sessionId":"trace-too-early","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0}"""
            },
            utcNowProvider: () => currentTime);

        var traceStartedAtUtc = currentTime.AddMilliseconds(-400);
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                traceStartedAtUtc,
                TimeSpan.FromMilliseconds(100),
                SessionId: "trace-too-early",
                IgnoreExpiry: true));

        currentTime = traceStartedAtUtc.AddMilliseconds(20);
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
                        senderName = "OldButton",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Button",
                        timestampUtc = traceStartedAtUtc.Subtract(TimeSpan.FromSeconds(2))
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
    public async Task Execute_GetMode_ShouldMergeReplayWhenEventTimestampIsInsideWindowButReplaySavedSlightlyLate()
    {
        const int processId = 43128;
        var now = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                sessionId = "trace-late-save",
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
                traceStartedAtUtc = now.AddMilliseconds(-900),
                effectiveDurationMs = 800,
                registrationCount = 1
            }));

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                now.AddMilliseconds(-900),
                TimeSpan.FromMilliseconds(800),
                SessionId: "trace-late-save"));
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
                        senderName = "LateReplaySaveButton",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Button",
                        timestampUtc = now.AddMilliseconds(-150)
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
    public async Task Execute_GetMode_ShouldMergeReplayWithoutTimestampUsingSavedAtUtcFallback()
    {
        const int processId = 43135;
        var currentTime = DateTimeOffset.UtcNow;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            new[]
            {
                """{"success":true,"sessionId":"trace-savedat-fallback","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0}"""
            },
            utcNowProvider: () => currentTime);

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                currentTime.AddMilliseconds(-400),
                TimeSpan.FromMilliseconds(100),
                SessionId: "trace-savedat-fallback",
                IgnoreExpiry: true));

        currentTime = currentTime.AddMilliseconds(200);
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
                        senderName = "SavedAtFallbackButton",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Button"
                    }
                }
            }));

        var traceTool = new TraceRoutedEventsTool(connected.SessionManager);

        var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get" }),
            CancellationToken.None));

        traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(1, traceResult.GetRawText());
        traceResult.GetProperty("events")[0].GetProperty("eventName").GetString().Should().Be("Click");
        traceResult.GetProperty("events")[0].GetProperty("sender").GetString().Should().Be("Button");
    }
}
