using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class EventHandlerTraceModeTests
{
    [Fact]
    public async Task TraceRoutedEvents_WithUppercaseGetMode_ShouldReturnGetPayload()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { mode = "GET" });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("mode").GetString().Should().Be("get");
    }

    [Fact]
    public async Task TraceRoutedEvents_WithWhitespaceWrappedGetMode_ShouldNormalizeValue()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { mode = "  GET  " });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("mode").GetString().Should().Be("get");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_WithZeroEventsWhileTracing_ShouldReturnCaptureWindowTooShortReason()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button();
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
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("captureWindowTooShort");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_WithMismatchedRequestedEvent_ShouldReturnFilterMismatchReason()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 250,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "MouseDown" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("filterMismatch");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_AfterTraceWindowEndsWithoutEvents_ShouldReturnEventNotRaisedReason()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 120,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        await Task.Delay(220);

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("eventNotRaised");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_WithZeroEvents_ShouldExposeRegistrationDiagnostics()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button { Name = "TraceDiagnosticsButton" };
        var elementId = finder.GenerateElementId(button);

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 120,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        await Task.Delay(220);

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("diagnostics").GetProperty("registrationCount").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("diagnostics").GetProperty("resolvedElementId").GetString().Should().Be(elementId);
        payload.GetProperty("diagnostics").GetProperty("resolvedElementType").GetString().Should().Be("Button");
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
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        buffer.Enqueue(new WatchEventRecord(
            EventType: "RoutedEvent",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: $"tool:routed:{elementId}:Click:test",
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
