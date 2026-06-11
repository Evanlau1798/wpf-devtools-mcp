using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed partial class EventHandlerTraceModeTests
{
    [StaFact]
    public async Task TraceRoutedEvents_GetMode_AfterAutoStop_WithMismatchedRequestedEvent_ShouldReturnFilterMismatchReason()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button { Name = "CompletedTraceMismatchButton" };
        var elementId = finder.GenerateElementId(button);

        var startResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 300,
                allowShortStartDuration = true
            }),
            CancellationToken.None);

        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
                .GetProperty("handlerCount").GetInt32() == 1,
            DispatcherSignalTimeout).Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        WaitForTraceCleanup(analyzer, button, elementId, DispatcherSignalTimeout).Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "MouseDown" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(0);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("filterMismatch");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_AfterAutoStopDeferredCleanupCompletes_ShouldReturnCompletedCleanupState()
    {
        var finder = new ElementFinder();
        var button = new System.Windows.Controls.Button { Name = "AutoStopCleanupFailureButton" };
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: static (_, _) => new TimeoutException("Simulated auto-stop cleanup timeout"));
        var handler = new EventHandlers(analyzer);
        var elementId = finder.GenerateElementId(button);

        var startResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 120,
                allowShortStartDuration = true
            }),
            CancellationToken.None);

        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        WaitForDeferredCleanupCompletedTrace(analyzer, button, DispatcherSignalTimeout).Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "Click" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(0);
        payload.GetProperty("cleanupState").GetString().Should().Be("deferredCompleted");
        payload.GetProperty("cleanupFailed").GetBoolean().Should().BeFalse();
        payload.GetProperty("cleanupIncomplete").GetBoolean().Should().BeFalse();
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("eventNotRaised");

        WaitForTraceCleanup(analyzer, button, elementId, DispatcherSignalTimeout).Should().BeTrue();
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_AfterAutoStopDeferredCleanupCompletes_WithMismatchedRequestedEvent_ShouldReturnFilterMismatchAndCleanupState()
    {
        var finder = new ElementFinder();
        var button = new System.Windows.Controls.Button { Name = "CleanupFailureMismatchButton" };
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: static (_, _) => new TimeoutException("Simulated auto-stop cleanup timeout"));
        var handler = new EventHandlers(analyzer);
        var elementId = finder.GenerateElementId(button);

        var startResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 120,
                allowShortStartDuration = true
            }),
            CancellationToken.None);

        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        WaitForDeferredCleanupCompletedTrace(analyzer, button, DispatcherSignalTimeout).Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "MouseDown" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("cleanupState").GetString().Should().Be("deferredCompleted");
        payload.GetProperty("cleanupFailed").GetBoolean().Should().BeFalse();
        payload.GetProperty("cleanupIncomplete").GetBoolean().Should().BeFalse();
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("filterMismatch");
        payload.GetProperty("diagnostics").GetProperty("requestedEventMismatch").GetBoolean().Should().BeTrue();
        payload.GetProperty("diagnostics").GetProperty("requestedEventName").GetString().Should().Be("MouseDown");
        payload.GetProperty("diagnostics").GetProperty("activeEventName").GetString().Should().Be("Click");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_WithMatchingSharedBufferEvent_ShouldSurfaceBufferedTraceEvidence()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());
        var analyzer = new EventAnalyzer(finder, buffer);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button { Name = "BufferedTraceButton" };
        var elementId = finder.GenerateElementId(button);

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 300,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
    var startPayload = JsonSerializer.SerializeToElement(startResult);
    startPayload.GetProperty("success").GetBoolean().Should().BeTrue();
    var sessionId = startPayload.GetProperty("sessionId").GetString();

        buffer.Enqueue(new WatchEventRecord(
            EventType: "RoutedEvent",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: $"event:{sessionId}:{elementId}:Click:test",
            ElementId: elementId,
            PropertyName: null,
            EventName: "Click",
            NewValue: null,
            ValueType: null,
            SenderType: "Button",
            SenderName: "BufferedTraceButton",
            RoutingStrategy: "Bubble",
            Handled: false,
            OriginalSourceType: "Button"));

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
        payload.GetProperty("eventCount").GetInt32().Should().Be(1, payload.GetRawText());
        payload.GetProperty("events")[0].GetProperty("eventName").GetString().Should().Be("Click");
        payload.GetProperty("events")[0].GetProperty("sender").GetString().Should().Be("Button");
        payload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_ShouldIgnoreSharedBufferEventsFromBeforeTraceStart()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());
        var analyzer = new EventAnalyzer(finder, buffer);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button { Name = "BufferedTraceButton" };
        var elementId = finder.GenerateElementId(button);

        buffer.Enqueue(new WatchEventRecord(
            EventType: "RoutedEvent",
            TimestampUtc: DateTimeOffset.UtcNow.AddSeconds(-5),
            SourceKey: $"tool:routed:{elementId}:Click:stale",
            ElementId: elementId,
            PropertyName: null,
            EventName: "Click",
            NewValue: null,
            ValueType: null,
            SenderType: "Button",
            SenderName: "BufferedTraceButton",
            RoutingStrategy: "Bubble",
            Handled: false,
            OriginalSourceType: "Button"));

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 300,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
        payload.GetProperty("eventCount").GetInt32().Should().Be(0, payload.GetRawText());
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("captureWindowTooShort");
    }
}
