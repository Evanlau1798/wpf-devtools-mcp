using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public partial class EventAnalyzerTests
{
    [StaFact]
    public void TraceRoutedEvents_AfterAutoStop_ShouldUnregisterHandlers()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button { Content = "AutoStop" };
        var elementId = finder.GenerateElementId(button);

        analyzer.TraceRoutedEvents(elementId, "Click", 100);

        WaitForTraceCleanup(analyzer, button, elementId, DispatcherSignalTimeout).Should().BeTrue();

        var trace = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventTrace()));
        trace.GetProperty("isTracing").GetBoolean().Should().BeFalse();

        var handlers = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "Click")));
        handlers.GetProperty("handlerCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void TraceRoutedEvents_WhenPreviousCleanupTimesOut_ShouldAllowReplacementWithoutReactivatingStaleHandlers()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: static (_, _) => new TimeoutException("Simulated cleanup timeout"));
        var button = new Button { Content = "CleanupFailure" };
        var elementId = finder.GenerateElementId(button);

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 1000)))
            .GetProperty("success").GetBoolean().Should().BeTrue();

        var replacementAttempt = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 1000)));

        replacementAttempt.GetProperty("success").GetBoolean().Should().BeTrue();

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()))
            .GetProperty("handlerInvocationCount").GetInt32().Should().Be(1,
                "stale handlers from the timed-out cleanup must not become active again after the replacement trace starts");

        button.Dispatcher.Invoke(() =>
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            while (DateTime.UtcNow < deadline)
            {
                var handlerCount = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "Click")))
                    .GetProperty("handlerCount").GetInt32();
                if (handlerCount == 1)
                {
                    return;
                }

                var frame = new DispatcherFrame();
                button.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }

            throw new TimeoutException("Timed out waiting for deferred routed event handler cleanup after replacement trace startup.");
        });

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()))
            .GetProperty("isTracing").GetBoolean().Should().BeTrue();

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "Click")))
            .GetProperty("handlerCount").GetInt32().Should().Be(1);
    }

    [StaFact]
    public void CleanupTraceSession_WhenEventArrivesDuringFailedTeardown_ShouldIgnoreTeardownEvent()
    {
        var finder = new ElementFinder();
        var button = new Button { Content = "TeardownBoundary" };
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: (_, _) =>
            {
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                return new TimeoutException("Simulated cleanup timeout");
            });
        var elementId = finder.GenerateElementId(button);

        var startOutcome = analyzer.StartTraceRoutedEvents(elementId, "Click", 1000);
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(startOutcome.Result))
            .GetProperty("success").GetBoolean().Should().BeTrue();

        analyzer.CleanupTraceSession(startOutcome.Session!, out var cleanupException).Should().BeFalse();
        cleanupException.Should().BeOfType<TimeoutException>();

        var tracePayload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()));
        tracePayload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        tracePayload.GetProperty("eventCount").GetInt32().Should().Be(0);
        tracePayload.GetProperty("handlerInvocationCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void StartTraceRoutedEvents_WithNegativeDuration_ShouldReturnInvalidArgumentError()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button { Content = "NegativeDuration" };
        var elementId = finder.GenerateElementId(button);

        var startOutcome = analyzer.StartTraceRoutedEvents(elementId, "Click", -1);
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(startOutcome.Result));

        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        startOutcome.Session.Should().BeNull();
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()))
            .GetProperty("isTracing").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public void StartTraceRoutedEvents_WithDurationAboveMaximum_ShouldCapEffectiveDuration()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button { Content = "LongTrace" };
        var elementId = finder.GenerateElementId(button);

        var startOutcome = analyzer.StartTraceRoutedEvents(elementId, "Click", 120000);
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(startOutcome.Result));

        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("duration").GetInt32().Should().Be(60000);
        startOutcome.Session.Should().NotBeNull();
        startOutcome.Session!.Metadata.EffectiveDurationMs.Should().Be(60000);
    }

    [StaFact]
    public void Dispose_WhenTraceStartupIsInProgress_ShouldCancelStartupAndRollbackRegistrations()
    {
        using var registrationStarted = new ManualResetEventSlim();
        using var allowRegistrationToFinish = new ManualResetEventSlim();
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: null,
            registrationInvoker: (uiElement, routedEvent, handler, _, registrations) =>
            {
                registrationStarted.Set();
                allowRegistrationToFinish.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                uiElement.AddHandler(routedEvent, handler, handledEventsToo: true);
                registrations.Add(new HandlerRegistration(uiElement, routedEvent, handler));
            });
        var button = new Button { Content = "StartupDisposeRace" };
        var elementId = finder.GenerateElementId(button);

        var startTask = Task.Factory.StartNew(
            () => analyzer.StartTraceRoutedEvents(elementId, "Click", 1000),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        registrationStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

        var disposeTask = Task.Run(() => analyzer.Dispose());
        disposeTask.Wait(TimeSpan.FromMilliseconds(200)).Should().BeFalse();
        WaitForStartCancellationRequest(analyzer, TimeSpan.FromSeconds(5)).Should().BeTrue();
        allowRegistrationToFinish.Set();

        WaitForTaskCompletion(disposeTask, button.Dispatcher, TimeSpan.FromSeconds(15)).Should().BeTrue();
        disposeTask.IsFaulted.Should().BeFalse();

        WaitForTaskCompletion(startTask, button.Dispatcher, TimeSpan.FromSeconds(15)).Should().BeTrue();

        var startPayload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(startTask.Result.Result));
        startPayload.GetProperty("success").GetBoolean().Should().BeFalse();
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()))
            .GetProperty("isTracing").GetBoolean().Should().BeFalse();
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "Click")))
            .GetProperty("handlerCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void CleanupActiveTraceSession_WhenCleanupAlreadyInProgress_ShouldFailClosed()
    {
        var finder = new ElementFinder();
        using var cleanupStarted = new ManualResetEventSlim();
        using var allowCleanupToFinish = new ManualResetEventSlim();
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: (dispatcher, removeHandlers) =>
            {
                cleanupStarted.Set();
                if (!allowCleanupToFinish.Wait(TimeSpan.FromSeconds(1)))
                {
                    return new TimeoutException("Timed out waiting to finish simulated cleanup.");
                }

                dispatcher!.Invoke(removeHandlers);
                return null;
            });
        var button = new Button { Content = "ConcurrentCleanup" };
        var elementId = finder.GenerateElementId(button);

        var startOutcome = analyzer.StartTraceRoutedEvents(elementId, "Click", 1000);
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(startOutcome.Result))
            .GetProperty("success").GetBoolean().Should().BeTrue();

        var cleanupTask = Task.Run(() => analyzer.CleanupTraceSession(startOutcome.Session!, out var cleanupException));

        cleanupStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

        analyzer.CleanupActiveTraceSession(out var concurrentCleanupException).Should().BeFalse();
        concurrentCleanupException.Should().BeOfType<InvalidOperationException>();
        concurrentCleanupException!.Message.Should().Contain("already in progress");

        allowCleanupToFinish.Set();
        WaitForTaskCompletion(cleanupTask, button.Dispatcher, TimeSpan.FromSeconds(1)).Should().BeTrue();
        cleanupTask.GetAwaiter().GetResult().Should().BeTrue();
    }

    [StaFact]
    public void TraceRoutedEvents_AfterAutoStop_ShouldUnregisterWindowHandlers()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        using var hostScope = WindowHostScope.Create();
        var window = hostScope.Window;
        var button = new Button { Content = "WindowHostedAutoStop" };

        window.Content = button;
        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

        var buttonElementId = finder.GenerateElementId(button);
        var windowElementId = finder.GenerateElementId(window);

        analyzer.TraceRoutedEvents(buttonElementId, "Click", 100);

        WaitForTraceCleanup(analyzer, button, buttonElementId, DispatcherSignalTimeout).Should().BeTrue();

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventHandlers(buttonElementId, "Click")))
            .GetProperty("handlerCount").GetInt32().Should().Be(0);

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventHandlers(windowElementId, "Click")))
            .GetProperty("handlerCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void TraceRoutedEvents_WhenPartialRegistrationFails_ShouldRollbackHandlersAndAllowRetry()
    {
        var finder = new ElementFinder();
        var failFirstRegistration = true;
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: null,
            registrationInvoker: (uiElement, routedEvent, handler, _, registrations) =>
            {
                uiElement.AddHandler(routedEvent, handler, handledEventsToo: true);
                registrations.Add(new HandlerRegistration(uiElement, routedEvent, handler));

                if (failFirstRegistration)
                {
                    failFirstRegistration = false;
                    throw new InvalidOperationException("Simulated partial registration failure");
                }
            });
        var button = new Button { Content = "RegistrationRetry" };
        var elementId = finder.GenerateElementId(button);

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 1000)))
            .GetProperty("errorCode").GetString().Should().Be("OperationFailed");

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "Click")))
            .GetProperty("handlerCount").GetInt32().Should().Be(0);

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()))
            .GetProperty("isTracing").GetBoolean().Should().BeFalse();

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 1000)))
            .GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [StaFact]
    public void StartTraceRoutedEvents_WhenRestartRegistrationFails_ShouldNotExposePreviousCompletedSnapshotByDefault()
    {
        var finder = new ElementFinder();
        var registrationAttempt = 0;
        var analyzer = new EventAnalyzer(
            finder,
            watchEventBuffer: null,
            cleanupInvoker: null,
            registrationInvoker: (uiElement, routedEvent, handler, _, registrations) =>
            {
                registrationAttempt++;
                if (registrationAttempt == 2)
                {
                    throw new InvalidOperationException("Simulated restart registration failure");
                }

                uiElement.AddHandler(routedEvent, handler, handledEventsToo: true);
                registrations.Add(new HandlerRegistration(uiElement, routedEvent, handler));
            });
        var button = new Button { Content = "RestartFailureDefaultSnapshot" };
        var elementId = finder.GenerateElementId(button);

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 1000)))
            .GetProperty("success").GetBoolean().Should().BeTrue();
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        analyzer.CleanupActiveTraceSession(out var cleanupException).Should().BeTrue();
        cleanupException.Should().BeNull();

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 1000)))
            .GetProperty("errorCode").GetString().Should().Be("OperationFailed");

        var tracePayload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()));
        tracePayload.GetProperty("sessionId").ValueKind.Should().Be(JsonValueKind.Null);
        tracePayload.GetProperty("eventCount").GetInt32().Should().Be(0);
        tracePayload.GetProperty("events").GetArrayLength().Should().Be(0);
        tracePayload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
    }

    private static bool WaitForTraceCleanup(EventAnalyzer analyzer, Button button, string elementId, TimeSpan timeout)
    {
        return button.Dispatcher.Invoke(() =>
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var tracePayload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()));
                var handlerPayload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "Click")));
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

    private static bool WaitForStartCancellationRequest(EventAnalyzer analyzer, TimeSpan timeout)
    {
        var transitionField = typeof(EventAnalyzer).GetField(
            "_traceTransitions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        transitionField.Should().NotBeNull();
        var transitions = transitionField!.GetValue(analyzer);
        transitions.Should().NotBeNull();
        var cancellationProperty = transitions!.GetType().GetProperty("CancelStartTransitionRequested");
        cancellationProperty.Should().NotBeNull();

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((bool)cancellationProperty!.GetValue(transitions)!)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
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
