using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class EventAnalyzerCleanupStatusTests
{
    [StaFact]
    public void TraceRoutedEvents_WhenPreviousCleanupIsDeferred_ShouldExposeCleanupIncompleteState()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: static (_, _) => new TimeoutException("Simulated previous cleanup timeout"));
        var button = new Button { Content = "CleanupPending" };
        var elementId = finder.GenerateElementId(button);

        JsonSerializer.SerializeToElement(analyzer.TraceRoutedEvents(elementId, "Click", 1000))
            .GetProperty("success").GetBoolean().Should().BeTrue();

        var replacementPayload = JsonSerializer.SerializeToElement(
            analyzer.TraceRoutedEvents(elementId, "Click", 1000));

        replacementPayload.GetProperty("success").GetBoolean().Should().BeTrue();
        replacementPayload.GetProperty("cleanupIncomplete").GetBoolean().Should().BeTrue();
        replacementPayload.GetProperty("cleanupState").GetString().Should().Be("deferredPending");
        replacementPayload.GetProperty("cleanupFailureType").GetString().Should().Be("TimeoutException");
        replacementPayload.GetProperty("cleanupFailureMessage").GetString()
            .Should().Contain("Simulated previous cleanup timeout");

        analyzer.Dispose();
        DrainDispatcher(button.Dispatcher);
    }

    [StaFact]
    public void CleanupTraceSession_WhenDeferredCleanupCompletes_ShouldExposeFinalCleanupState()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: static (_, _) => new TimeoutException("Simulated cleanup timeout"));
        var button = new Button { Content = "CleanupFinalState" };
        var elementId = finder.GenerateElementId(button);
        var startOutcome = analyzer.StartTraceRoutedEvents(
            elementId,
            "Click",
            duration: 1000,
            scheduleAutoStop: false);

        JsonSerializer.SerializeToElement(startOutcome.Result)
            .GetProperty("success").GetBoolean().Should().BeTrue();

        analyzer.CleanupTraceSession(startOutcome.Session!, out var cleanupException).Should().BeFalse();
        cleanupException.Should().BeOfType<TimeoutException>();

        var pendingPayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace(startOutcome.Session!));
        pendingPayload.GetProperty("cleanupIncomplete").GetBoolean().Should().BeTrue();
        pendingPayload.GetProperty("cleanupState").GetString().Should().Be("deferredPending");

        DrainDispatcher(button.Dispatcher);

        var completedPayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace(startOutcome.Session!));
        completedPayload.GetProperty("cleanupFailed").GetBoolean().Should().BeFalse();
        completedPayload.GetProperty("cleanupIncomplete").GetBoolean().Should().BeFalse();
        completedPayload.GetProperty("cleanupState").GetString().Should().Be("deferredCompleted");

        analyzer.Dispose();
        DrainDispatcher(button.Dispatcher);
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
