using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
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

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
    }
}
