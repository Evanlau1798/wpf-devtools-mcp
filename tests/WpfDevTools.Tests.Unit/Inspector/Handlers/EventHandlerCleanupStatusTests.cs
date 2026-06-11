using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class EventHandlerCleanupStatusTests
{
    [StaFact]
    public async Task TraceRoutedEvents_GetMode_AfterDeferredCleanupCompletes_ShouldExposeCompletedCleanupState()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: static (_, _) => new TimeoutException("Simulated cleanup timeout"));
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "HandlerCleanupStateButton" };
        var elementId = finder.GenerateElementId(button);
        var startOutcome = analyzer.StartTraceRoutedEvents(
            elementId,
            "Click",
            duration: 1000,
            scheduleAutoStop: false);

        JsonSerializer.SerializeToElement(startOutcome.Result)
            .GetProperty("success").GetBoolean().Should().BeTrue();
        analyzer.CleanupTraceSession(startOutcome.Session!, out _).Should().BeFalse();

        DrainDispatcher(button.Dispatcher);

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "Click" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("cleanupState").GetString().Should().Be("deferredCompleted");
        payload.GetProperty("cleanupFailed").GetBoolean().Should().BeFalse();
        payload.GetProperty("cleanupIncomplete").GetBoolean().Should().BeFalse();
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
