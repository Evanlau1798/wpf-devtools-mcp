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

    [Fact]
    public async Task TraceRoutedEvents_CaptureMode_WithNegativeDuration_ShouldRejectBeforeStartingTrace()
    {
        var analyzer = new EventAnalyzer(new ElementFinder());
        var handler = new EventHandlers(analyzer);

        Func<Task> act = () => handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "capture",
                eventName = "Click",
                duration = -1
            }),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public async Task TraceRoutedEvents_StartMode_WithDurationAboveMaximum_ShouldReturnCappedEffectiveDuration()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 120000
            }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("requestedDuration").GetInt32().Should().Be(120000);
        payload.GetProperty("effectiveDuration").GetInt32().Should().Be(60000);
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
        payload.GetProperty("diagnostics").GetProperty("windowExpiredBeforeGet").GetBoolean().Should().BeFalse();
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
        payload.GetProperty("diagnostics").GetProperty("windowExpiredBeforeGet").GetBoolean().Should().BeTrue();
        payload.GetProperty("diagnostics").GetProperty("expiredByMs").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("diagnostics").GetProperty("windowEndedAtUtc").GetDateTimeOffset().Should().BeBefore(DateTimeOffset.UtcNow);
        payload.GetProperty("diagnostics").GetProperty("getRequestedAtUtc").GetDateTimeOffset().Should().BeAfter(
            payload.GetProperty("diagnostics").GetProperty("windowEndedAtUtc").GetDateTimeOffset());
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
        payload.GetProperty("activeEventName").GetString().Should().Be("Click");
        payload.GetProperty("resolvedElementId").GetString().Should().Be(elementId);
        payload.GetProperty("resolvedElementType").GetString().Should().Be("Button");
        payload.GetProperty("traceStartedAtUtc").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("effectiveDurationMs").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("registrationCount").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("diagnostics").GetProperty("registrationCount").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("diagnostics").GetProperty("resolvedElementId").GetString().Should().Be(elementId);
        payload.GetProperty("diagnostics").GetProperty("resolvedElementType").GetString().Should().Be("Button");
    }

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
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

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
    public async Task TraceRoutedEvents_GetMode_AfterAutoStopCleanupFailure_ShouldReturnCleanupFailedReason()
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

        WaitForCleanupFailedTrace(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "Click" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(0);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("cleanupFailed");
        payload.GetProperty("diagnostics").GetProperty("cleanupFailureType").GetString().Should().Be("TimeoutException");

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_AfterAutoStopCleanupFailure_WithMismatchedRequestedEvent_ShouldStillReturnCleanupFailedReason()
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

        WaitForCleanupFailedTrace(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "MouseDown" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("cleanupFailed");
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

    [StaFact]
    public void TraceRoutedEvents_CaptureModeCancellation_ShouldStopTracing()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "CancelledTraceButton" };
        var elementId = finder.GenerateElementId(button);
        using var cts = new CancellationTokenSource();

        var traceTask = handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            }),
            cts.Token);

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventTrace()).GetProperty("isTracing").GetBoolean(),
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        cts.Cancel();

        WaitForTaskCompletion(traceTask, button.Dispatcher, TimeSpan.FromSeconds(1)).Should().BeTrue();
        Assert.ThrowsAny<OperationCanceledException>(() => traceTask.GetAwaiter().GetResult());

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

        var payload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
        payload.GetProperty("isTracing").GetBoolean().Should().BeFalse();

        JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
            .GetProperty("handlerCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public async Task TraceRoutedEvents_CaptureMode_WhenCleanupFails_ShouldReturnCapturedEventsWithCleanupDiagnostics()
    {
        var finder = new ElementFinder();
        var button = new Button { Name = "CaptureCleanupFailureButton" };
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: static (_, _) => new TimeoutException("Simulated capture cleanup timeout"));
        var handler = new EventHandlers(analyzer);
        var elementId = finder.GenerateElementId(button);

        var traceTask = handler.HandleAsync(
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
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        var result = await traceTask;

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
        payload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        payload.GetProperty("eventCount").GetInt32().Should().Be(1);
        payload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(1);
        payload.GetProperty("events")[0].GetProperty("eventName").GetString().Should().Be("Click");
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("cleanupFailed");
        payload.GetProperty("diagnostics").GetProperty("cleanupFailureType").GetString().Should().Be("TimeoutException");
        payload.GetProperty("diagnostics").GetProperty("message").GetString().Should().Contain("Simulated capture cleanup timeout");

        var tracePayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
    tracePayload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        tracePayload.GetProperty("eventCount").GetInt32().Should().Be(1);
        tracePayload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(1);

        JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
            .GetProperty("handlerCount").GetInt32().Should().Be(1);
    }

    [StaFact]
    public async Task TraceRoutedEvents_CaptureModeCancellation_WhenCleanupFails_ShouldReturnFrozenCleanupFailedPayload()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: static (_, _) => new TimeoutException("Simulated cancellation cleanup timeout"));
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "CancelledCaptureCleanupFailureButton" };
        var elementId = finder.GenerateElementId(button);
        using var cts = new CancellationTokenSource();

        var traceTask = handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            }),
            cts.Token);

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventTrace()).GetProperty("isTracing").GetBoolean(),
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        cts.Cancel();

        WaitForTaskCompletion(traceTask, button.Dispatcher, TimeSpan.FromSeconds(1)).Should().BeTrue();

        var payload = JsonSerializer.SerializeToElement(await traceTask);
        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
        payload.GetProperty("sessionId").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("cleanupFailed");
        payload.GetProperty("diagnostics").GetProperty("cleanupFailureType").GetString().Should().Be("TimeoutException");
        payload.GetProperty("diagnostics").GetProperty("suggestedAction").GetString().Should().Contain("Use get mode");
    }

    [StaFact]
    public async Task TraceRoutedEvents_CaptureModeCancellation_WhenCleanupIsDeferred_ShouldEventuallyUnregisterHandlers()
    {
        var finder = new ElementFinder();
        var button = new Button { Name = "DeferredCancellationCleanupButton" };
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: (dispatcher, removeHandlers) =>
            {
                dispatcher!.BeginInvoke(DispatcherPriority.Background, removeHandlers);
                return new TimeoutException("Simulated deferred cancellation cleanup timeout");
            });
        var handler = new EventHandlers(analyzer);
        var elementId = finder.GenerateElementId(button);
        using var cts = new CancellationTokenSource();

        var traceTask = handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            }),
            cts.Token);

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventTrace()).GetProperty("isTracing").GetBoolean(),
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        cts.Cancel();

        WaitForTaskCompletion(traceTask, button.Dispatcher, TimeSpan.FromSeconds(1)).Should().BeTrue();

        var payload = JsonSerializer.SerializeToElement(await traceTask);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("cleanupFailed");

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

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
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        var secondStartResult = handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            }),
            CancellationToken.None).GetAwaiter().GetResult();

        JsonSerializer.SerializeToElement(secondStartResult).GetProperty("success").GetBoolean().Should().BeTrue();
        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeTrue();

        firstRequestCts.Cancel();

        WaitForTaskCompletion(firstTraceTask, button.Dispatcher, TimeSpan.FromSeconds(1)).Should().BeTrue();
        Assert.ThrowsAny<OperationCanceledException>(() => firstTraceTask.GetAwaiter().GetResult());

        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        var tracePayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
        tracePayload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(1);

        JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
            .GetProperty("handlerCount").GetInt32().Should().Be(1);
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
            TimeSpan.FromSeconds(1)).Should().BeTrue();

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
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
                .GetProperty("handlerInvocationCount").GetInt32() == 1,
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        WaitForTaskCompletion(captureTask, button.Dispatcher, TimeSpan.FromSeconds(2)).Should().BeTrue();

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
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

        var getPayload = JsonSerializer.SerializeToElement(await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "Click" }),
            CancellationToken.None));

        getPayload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        getPayload.GetProperty("eventCount").GetInt32().Should().Be(1);
        getPayload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(1);
    }

    private static bool WaitForTraceCleanup(EventAnalyzer analyzer, Button button, string elementId, TimeSpan timeout)
    {
        return button.Dispatcher.Invoke(() =>
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var tracePayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
                var handlerPayload = JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"));
                if (!tracePayload.GetProperty("isTracing").GetBoolean()
                    && handlerPayload.GetProperty("handlerCount").GetInt32() == 0)
                {
                    return true;
                }

                var frame = new DispatcherFrame();
                button.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }

            return false;
        });
    }

    private static bool WaitForCleanupFailedTrace(EventAnalyzer analyzer, Button button, string elementId, TimeSpan timeout)
    {
        return button.Dispatcher.Invoke(() =>
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var tracePayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
                if (!tracePayload.GetProperty("isTracing").GetBoolean()
                    && tracePayload.TryGetProperty("cleanupFailed", out var cleanupFailed)
                    && cleanupFailed.GetBoolean())
                {
                    return true;
                }

                var frame = new DispatcherFrame();
                button.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }

            return false;
        });
    }

    private static bool WaitForTaskCompletion(Task task, Dispatcher dispatcher, TimeSpan timeout)
    {
        return dispatcher.Invoke(() =>
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }

            return task.IsCompleted;
        });
    }
}
