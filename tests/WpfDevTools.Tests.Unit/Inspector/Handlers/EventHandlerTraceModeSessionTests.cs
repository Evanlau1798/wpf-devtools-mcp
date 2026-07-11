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
    public void TraceRoutedEvents_CaptureModeCancellation_ShouldNotStopNewerTraceSession()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "OverlappingTraceButton" };
        var elementId = finder.GenerateElementId(button);
        using var firstRequestCts = new CancellationTokenSource();

        var firstTraceTask = handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            }),
            firstRequestCts.Token);

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventTrace()).GetProperty("isTracing").GetBoolean(),
            DispatcherSignalTimeout).Should().BeTrue();

        var secondStartResult = handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 5000,
                allowShortStartDuration = true
            }),
            CancellationToken.None).GetAwaiter().GetResult();

        JsonSerializer.SerializeToElement(secondStartResult).GetProperty("success").GetBoolean().Should().BeTrue();
        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeTrue();

        firstRequestCts.Cancel();

        WaitForTaskCompletion(firstTraceTask, button.Dispatcher, DispatcherSignalTimeout).Should().BeTrue();
        Assert.ThrowsAny<OperationCanceledException>(() => firstTraceTask.GetAwaiter().GetResult());

        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        var tracePayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
        tracePayload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(1);

        JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
            .GetProperty("handlerCount").GetInt32().Should().Be(1);

        analyzer.CleanupActiveTraceSession(out var cleanupException).Should().BeTrue();
        cleanupException.Should().BeNull();
    }

    [StaFact]
    public async Task TraceRoutedEvents_CaptureModeCompletion_ShouldNotReturnNewerTraceSessionData()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());
        var analyzer = new EventAnalyzer(finder, buffer);
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "SupersededTraceButton" };
        var elementId = finder.GenerateElementId(button);

        var firstCaptureTask = handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            }),
            CancellationToken.None);

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventTrace()).GetProperty("isTracing").GetBoolean(),
            DispatcherSignalTimeout).Should().BeTrue();

        var secondStartResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            }),
            CancellationToken.None);

    var secondStartPayload = JsonSerializer.SerializeToElement(secondStartResult);
    secondStartPayload.GetProperty("success").GetBoolean().Should().BeTrue();
    var newerSessionId = secondStartPayload.GetProperty("sessionId").GetString();

        buffer.Enqueue(new WatchEventRecord(
            EventType: "RoutedEvent",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: $"event:{newerSessionId}:{elementId}:Click:newer-session",
            ElementId: elementId,
            PropertyName: null,
            EventName: "Click",
            NewValue: null,
            ValueType: null,
            SenderType: "Button",
            SenderName: "SupersededTraceButton",
            RoutingStrategy: "Bubble",
            Handled: false,
            OriginalSourceType: "Button"));

        var firstCapturePayload = JsonSerializer.SerializeToElement(await firstCaptureTask);
        firstCapturePayload.GetProperty("success").GetBoolean().Should().BeTrue();
        firstCapturePayload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        firstCapturePayload.GetProperty("eventCount").GetInt32().Should().Be(0);
        firstCapturePayload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void TraceRoutedEvents_CaptureModeCompletion_ShouldReturnStableHandlerInvocationCount()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "CompletedCaptureButton" };
        var elementId = finder.GenerateElementId(button);

        var captureTask = handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                elementId,
                eventName = "Click",
                duration = 150,
                allowShortStartDuration = true
            }),
            CancellationToken.None);

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
                .GetProperty("handlerCount").GetInt32() == 1,
            DispatcherSignalTimeout).Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
                .GetProperty("handlerInvocationCount").GetInt32() == 1,
            DispatcherSignalTimeout).Should().BeTrue();

        WaitForTaskCompletion(captureTask, button.Dispatcher, DispatcherSignalTimeout).Should().BeTrue();

        var capturePayload = JsonSerializer.SerializeToElement(captureTask.GetAwaiter().GetResult());
        capturePayload.GetProperty("success").GetBoolean().Should().BeTrue();
        capturePayload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        capturePayload.GetProperty("eventCount").GetInt32().Should().Be(1);
        capturePayload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(1);
        capturePayload.GetProperty("events")[0].GetProperty("eventName").GetString().Should().Be("Click");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_AfterAutoStop_ShouldPreserveHandlerInvocationCount()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "GetModeCompletedTraceButton" };
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

        var getPayload = JsonSerializer.SerializeToElement(await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "Click" }),
            CancellationToken.None));

        getPayload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        getPayload.GetProperty("eventCount").GetInt32().Should().Be(1);
        getPayload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(1);
    }
}
