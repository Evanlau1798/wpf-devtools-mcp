using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class TraceRoutedEventsToolReplayTests
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
        var currentTime = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);
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
                currentTime.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(150),
                SessionId: "trace-savedat-fallback"));

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

    private sealed class ConnectedTraceReplaySession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

        public static async Task<ConnectedTraceReplaySession> CreateAsync(
            int processId,
            string[] responses,
            Action<InspectorRequest>? inspectRequest = null,
            Func<DateTimeOffset>? utcNowProvider = null)
        {
            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var responseQueue = new Queue<string>(responses);

            var serverTask = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                try
                {
                    while (server.IsConnected && responseQueue.Count > 0)
                    {
                        var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                        var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                        inspectRequest?.Invoke(request);

                        var response = new InspectorResponse
                        {
                            Id = request.Id,
                            CorrelationId = request.CorrelationId,
                            Result = JsonSerializer.Deserialize<JsonElement>(responseQueue.Dequeue())
                        };

                        await MessageFraming.WriteMessageAsync(
                            server,
                            JsonSerializer.Serialize(response),
                            CancellationToken.None);
                    }
                }
                catch (EndOfStreamException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            });

            var sessionManager = new SessionManager(
                McpServerConfiguration.RateLimitRequestsPerMinute,
                authManager: null,
                certManager: null,
                utcNowProvider: utcNowProvider);
            DisableCleanupTimer(sessionManager);
            sessionManager.AddSession(processId);
            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplacePipeClient(sessionManager, processId, client);

            return new ConnectedTraceReplaySession(sessionManager, server, serverTask);
        }

        public static Task<ConnectedTraceReplaySession> CreateAsync(int processId, params string[] responses) =>
            CreateAsync(processId, responses, inspectRequest: null, utcNowProvider: null);

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
                serverTask.GetAwaiter().GetResult();
            }
            finally
            {
                SessionManager.Dispose();
                server.Dispose();
            }
        }

        private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
        {
            var field = typeof(SessionManager).GetField("_pipeClients", BindingFlags.Instance | BindingFlags.NonPublic);
            var pipeClients = field!.GetValue(sessionManager) as Dictionary<int, NamedPipeClient>;
            if (pipeClients!.TryGetValue(processId, out var existingClient))
            {
                existingClient.Dispose();
            }

            pipeClients[processId] = replacement;
        }

        private static void DisableCleanupTimer(SessionManager sessionManager)
        {
            var timerField = typeof(SessionManager).GetField("_cleanupTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            var timer = timerField!.GetValue(sessionManager) as System.Threading.Timer;
            timer.Should().NotBeNull();
            timer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
