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
            DispatcherSignalTimeout).Should().BeTrue();

        cts.Cancel();

        WaitForTaskCompletion(traceTask, button.Dispatcher, DispatcherSignalTimeout).Should().BeTrue();
        Assert.ThrowsAny<OperationCanceledException>(() => traceTask.GetAwaiter().GetResult());

        WaitForTraceCleanup(analyzer, button, elementId, DispatcherSignalTimeout).Should().BeTrue();

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
            DispatcherSignalTimeout).Should().BeTrue();

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
            DispatcherSignalTimeout).Should().BeTrue();

        cts.Cancel();

        WaitForTaskCompletion(traceTask, button.Dispatcher, DispatcherSignalTimeout).Should().BeTrue();

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
            DispatcherSignalTimeout).Should().BeTrue();

        cts.Cancel();

        WaitForTaskCompletion(traceTask, button.Dispatcher, DispatcherSignalTimeout).Should().BeTrue();

        var payload = JsonSerializer.SerializeToElement(await traceTask);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("cleanupFailed");

        WaitForTraceCleanup(analyzer, button, elementId, DispatcherSignalTimeout).Should().BeTrue();
    }
}
