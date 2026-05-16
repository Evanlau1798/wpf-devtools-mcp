using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public sealed class RequestDispatcherDisposeTraceCleanupTests
{
    [StaFact]
    public void Dispose_WithActiveTraceSession_ShouldStopTracing()
    {
        using var logger = new FileLogger();
        var dispatcher = new RequestDispatcher(logger);
        var finder = GetElementFinder(dispatcher);
        var analyzer = GetEventAnalyzer(dispatcher);
        var button = new Button { Name = "DispatcherTraceButton" };
        var elementId = finder.GenerateElementId(button);

        var response = StartTrace(dispatcher, elementId, "trace-dispose");

        response.Error.Should().BeNull();
        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeTrue();

        dispatcher.Dispose();

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

        var payload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
        payload.GetProperty("isTracing").GetBoolean().Should().BeFalse();

        JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
            .GetProperty("handlerCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void Dispose_WhenEventTraceCleanupTimesOut_ShouldDeferRemovalAndNotThrow()
    {
        using var logger = new FileLogger();
        var dispatcher = new RequestDispatcher(
            logger,
            static (_, _) => new TimeoutException("Simulated dispatcher cleanup timeout"));
        var finder = GetElementFinder(dispatcher);
        var analyzer = GetEventAnalyzer(dispatcher);
        var button = new Button { Name = "DispatcherTraceCleanupFailureButton" };
        var elementId = finder.GenerateElementId(button);

        var response = StartTrace(dispatcher, elementId, "trace-dispose-failure");

        response.Error.Should().BeNull();

        Action dispose = () => dispatcher.Dispose();
        dispose.Should().NotThrow();

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeFalse();
        JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
            .GetProperty("handlerCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void Dispose_WhenEventTraceCleanupIsDeferred_ShouldNotThrowAndShouldEventuallyUnregisterHandlers()
    {
        using var logger = new FileLogger();
        var dispatcher = new RequestDispatcher(
            logger,
            static (traceDispatcher, removeHandlers) =>
            {
                traceDispatcher!.BeginInvoke(DispatcherPriority.Background, removeHandlers);
                return new TimeoutException("Simulated deferred dispatcher cleanup timeout");
            });
        var finder = GetElementFinder(dispatcher);
        var analyzer = GetEventAnalyzer(dispatcher);
        var button = new Button { Name = "DeferredDispatcherTraceCleanupButton" };
        var elementId = finder.GenerateElementId(button);

        var response = StartTrace(dispatcher, elementId, "trace-dispose-deferred-cleanup");

        response.Error.Should().BeNull();

        Action dispose = () => dispatcher.Dispose();
        dispose.Should().NotThrow();

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeFalse();
        JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
            .GetProperty("handlerCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void Dispose_WhenDispatcherShutdownPreventsTraceCleanup_ShouldNotThrowAndShouldClearTraceState()
    {
        using var logger = new FileLogger();
        var dispatcher = new RequestDispatcher(
            logger,
            static (_, _) => new InvalidOperationException("The dispatcher has shut down and is unavailable during teardown."));
        var finder = GetElementFinder(dispatcher);
        var analyzer = GetEventAnalyzer(dispatcher);
        var button = new Button { Name = "DispatcherShutdownCleanupButton" };
        var elementId = finder.GenerateElementId(button);

        var response = StartTrace(dispatcher, elementId, "trace-dispose-dispatcher-shutdown");

        response.Error.Should().BeNull();

        Action dispose = () => dispatcher.Dispose();
        dispose.Should().NotThrow();

        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public void Dispose_WhenWrappedDispatcherShutdownPreventsTraceCleanup_ShouldNotThrowAndShouldClearTraceState()
    {
        using var logger = new FileLogger();
        var dispatcher = new RequestDispatcher(
            logger,
            static (_, _) => new AggregateException(
                new InvalidOperationException(
                    "Failed to remove routed event handler 'Click' from 'Button'.",
                    new InvalidOperationException("The dispatcher has shut down and is unavailable during teardown."))));
        var finder = GetElementFinder(dispatcher);
        var analyzer = GetEventAnalyzer(dispatcher);
        var button = new Button { Name = "WrappedDispatcherShutdownCleanupButton" };
        var elementId = finder.GenerateElementId(button);

        var response = StartTrace(dispatcher, elementId, "trace-dispose-wrapped-dispatcher-shutdown");

        response.Error.Should().BeNull();

        Action dispose = () => dispatcher.Dispose();
        dispose.Should().NotThrow();

        JsonSerializer.SerializeToElement(analyzer.GetEventTrace())
            .GetProperty("isTracing").GetBoolean().Should().BeFalse();
    }

    private static InspectorResponse StartTrace(RequestDispatcher dispatcher, string elementId, string requestId)
    {
        return dispatcher.DispatchAsync(new InspectorRequest
        {
            Id = requestId,
            Method = "trace_routed_events",
            Params = JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            })
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static ElementFinder GetElementFinder(RequestDispatcher dispatcher)
    {
        return (ElementFinder)typeof(RequestDispatcher)
            .GetField("_elementFinder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(dispatcher)!;
    }

    private static EventAnalyzer GetEventAnalyzer(RequestDispatcher dispatcher)
    {
        var handlerMap = (System.Collections.IDictionary)typeof(RequestDispatcher)
            .GetField("_handlerMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(dispatcher)!;

        var eventHandler = (EventHandlers)handlerMap["trace_routed_events"]!;
        return (EventAnalyzer)typeof(EventHandlers)
            .GetField("_eventAnalyzer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(eventHandler)!;
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
}
