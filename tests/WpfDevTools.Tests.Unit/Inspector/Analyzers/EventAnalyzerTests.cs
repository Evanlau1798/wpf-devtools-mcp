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

public class EventAnalyzerTests
{

    [StaFact]
    public void TraceRoutedEvents_WithValidElement_ShouldStartTracing()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.TraceRoutedEvents(elementId, "Click", 1000);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("message");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void FireRoutedEvent_WithValidEvent_ShouldRaiseEvent()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var eventFired = false;
        button.Click += (s, e) => eventFired = true;

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "Click", null);

        // Assert
        eventFired.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [StaFact]
    public void GetEventTrace_ShouldReturnTraceData()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        // Act
        var result = analyzer.GetEventTrace();

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("events");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void GetEventHandlers_WithEventHandlers_ShouldReturnHandlers()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        button.Click += (s, e) => { };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetEventHandlers(elementId, "Click");

        // Assert
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("handlerCount").GetInt32().Should().BeGreaterThan(0);
    }

    [StaFact]
    public void GetEventHandlers_WithAttachedClickHandler_ShouldReturnSuccessAndHandlerMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        button.Click += OnButtonClick;
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetEventHandlers(elementId, "Click");

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("handlerCount").GetInt32().Should().BeGreaterThan(0);
        doc.GetProperty("handlers")[0].GetProperty("methodName").GetString().Should().Be(nameof(OnButtonClick));
    }

    [StaFact]
    public void GetEventHandlers_WithoutAttachedHandlers_ShouldReturnSuccessWithEmptyCollection()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetEventHandlers(elementId, "Click");

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("handlerCount").GetInt32().Should().Be(0);
        doc.GetProperty("handlers").GetArrayLength().Should().Be(0);
    }

    [StaFact]
    public void TraceRoutedEvents_WithWindowContext_ThenClickCheckBox_ShouldCaptureEvents()
    {
        var finder = new ElementFinder();
        var eventAnalyzer = new EventAnalyzer(finder);
        var interactionAnalyzer = new InteractionAnalyzer(finder);

        var window = new Window { Width = 200, Height = 200 };
        var checkBox = new CheckBox { Content = "Test" };
        window.Content = checkBox;
        window.Show();

        try
        {
            var cbId = finder.GenerateElementId(checkBox);
            finder.GenerateElementId(window);

            // Start tracing
            var startResult = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(eventAnalyzer.TraceRoutedEvents(cbId, "Click", 5000)));
            startResult.GetProperty("success").GetBoolean().Should().BeTrue();

            // Click
            var clickResult = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(interactionAnalyzer.ClickElement(cbId)));
            clickResult.GetProperty("success").GetBoolean().Should().BeTrue();

            Thread.Sleep(50);

            // Get trace - should have captured events
            var trace = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(eventAnalyzer.GetEventTrace()));
            trace.GetProperty("success").GetBoolean().Should().BeTrue();
            trace.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0,
                "Click event should be captured with dual registration on element + root window");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetEventHandlers_WithInvalidEvent_ShouldReturnAvailableEvents()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "NonExistentEvent")));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.TryGetProperty("availableEvents", out var events).Should().BeTrue();
        events.GetArrayLength().Should().BeGreaterThan(0);
    }

    [StaFact]
    public void TraceRoutedEvents_WithClickOnNonButtonElement_ShouldFindEventViaGlobalSearch()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var grid = new System.Windows.Controls.Grid();
        var elementId = finder.GenerateElementId(grid);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 1000)));

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            "Click event should be findable on any UIElement via global RoutedEvent search");
    }

    [StaFact]
    public void TraceRoutedEvents_WithMouseDown_ShouldFindEventAndStartTracing()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "MouseDown", 1000)));

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            "MouseDown event should be traceable");
    }

    [StaFact]
    public void GetEventTrace_ShouldIncludeDiagnosticFields()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        var trace = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventTrace()));

        trace.GetProperty("success").GetBoolean().Should().BeTrue();
        trace.TryGetProperty("handlerInvocationCount", out _).Should().BeTrue(
            "GetEventTrace should include diagnostic handlerInvocationCount");
    }

    [StaFact]
    public void TraceRoutedEvents_OnWindow_WithClick_ShouldFindEventViaGlobalSearch()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var elementId = finder.GenerateElementId(window);

        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 1000)));

            result.GetProperty("success").GetBoolean().Should().BeTrue(
                "Click event should be findable on Window via global search (not just type hierarchy)");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void TraceRoutedEvents_WithMouseDownOnWindow_ThenFireEvent_ShouldCaptureEvent()
    {
        var finder = new ElementFinder();
        var eventAnalyzer = new EventAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var button = new Button { Content = "Test" };
        window.Content = button;
        window.Show();

        try
        {
            var btnId = finder.GenerateElementId(button);

            var startResult = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(eventAnalyzer.TraceRoutedEvents(btnId, "MouseDown", 5000)));
            startResult.GetProperty("success").GetBoolean().Should().BeTrue();

            // Fire MouseDown directly
            button.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0,
                System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = System.Windows.UIElement.MouseDownEvent
            });

            Thread.Sleep(50);

            var trace = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(eventAnalyzer.GetEventTrace()));
            trace.GetProperty("success").GetBoolean().Should().BeTrue();
            trace.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0,
                "MouseDown event should be captured by trace handler");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void TraceRoutedEvents_AfterAutoStop_ShouldUnregisterHandlers()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button { Content = "AutoStop" };
        var elementId = finder.GenerateElementId(button);

        analyzer.TraceRoutedEvents(elementId, "Click", 100);

        WaitForTraceCleanup(analyzer, button, elementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

        var trace = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventTrace()));
        trace.GetProperty("isTracing").GetBoolean().Should().BeFalse();

        var handlers = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "Click")));
        handlers.GetProperty("handlerCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void TraceRoutedEvents_WhenPreviousCleanupFails_ShouldKeepExistingTraceActiveAndRejectReplacement()
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

        replacementAttempt.GetProperty("errorCode").GetString().Should().Be("OperationFailed");
        replacementAttempt.GetProperty("error").GetString().Should().Contain("Failed to stop existing event trace");

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()))
            .GetProperty("isTracing").GetBoolean().Should().BeFalse();

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
        allowRegistrationToFinish.Set();

        disposeTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        disposeTask.IsFaulted.Should().BeFalse();

        startTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

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

        WaitForTraceCleanup(analyzer, button, buttonElementId, TimeSpan.FromSeconds(1)).Should().BeTrue();

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

    [StaFact]
    public void FireRoutedEvent_Click_OnButtonBase_ShouldExecuteCommand()
    {
        // Arrange - button with ICommand binding
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var commandExecuted = false;
        var button = new Button
        {
            Command = new TestRelayCommand(() => commandExecuted = true)
        };
        var elementId = finder.GenerateElementId(button);

        // Act
        analyzer.FireRoutedEvent(elementId, "Click", null);

        // Assert - OnClick() should execute the Command
        commandExecuted.Should().BeTrue(
            "fire_routed_event('Click') on ButtonBase should call OnClick() which executes ICommand");
    }

    [StaFact]
    public void FireRoutedEvent_NonClick_OnButton_ShouldNotUseOnClick()
    {
        // Arrange - non-Click events should use RaiseEvent, not OnClick
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var commandExecuted = false;
        var button = new Button
        {
            Command = new TestRelayCommand(() => commandExecuted = true)
        };
        var elementId = finder.GenerateElementId(button);

        // Act - fire a non-Click event (LostFocus accepts RoutedEventArgs)
        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.FireRoutedEvent(elementId, "LostFocus", null)));

        // Assert - Command should NOT be executed (OnClick not called)
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        commandExecuted.Should().BeFalse(
            "Non-Click events should use RaiseEvent, not OnClick");
        result.TryGetProperty("usedOnClick", out _).Should().BeFalse(
            "usedOnClick flag should not be present for non-Click events");
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
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

    private sealed class TestRelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public TestRelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
