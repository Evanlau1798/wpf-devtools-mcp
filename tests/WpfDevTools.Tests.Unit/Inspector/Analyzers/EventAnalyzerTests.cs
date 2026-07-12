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

[Collection("TimingSensitive")]
public partial class EventAnalyzerTests
{
    private static readonly TimeSpan DispatcherSignalTimeout = TimeSpan.FromSeconds(10);

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
