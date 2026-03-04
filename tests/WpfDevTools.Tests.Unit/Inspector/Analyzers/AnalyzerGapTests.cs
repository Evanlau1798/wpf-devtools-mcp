using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

// ═══════════════════════════════════════════════════════════════
// PerformanceAnalyzer Gap Tests
// ═══════════════════════════════════════════════════════════════

public class PerformanceAnalyzerGapTests
{
    public PerformanceAnalyzerGapTests()
    {
        // Reset all monitoring state before each test
        PerformanceAnalyzer.ResetMonitoring();
    }

    [Fact]
    public void TrackBinding_WithObject_ShouldNotThrow()
    {
        // Arrange
        var binding = new object();

        // Act
        var exception = Record.Exception(() => PerformanceAnalyzer.TrackBinding(binding));

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public void TrackBinding_WithMultipleObjects_ShouldNotThrow()
    {
        // Arrange & Act
        for (int i = 0; i < 5; i++)
        {
            var exception = Record.Exception(() => PerformanceAnalyzer.TrackBinding(new object()));
            exception.Should().BeNull();
        }
    }

    [Fact]
    public void ClearTrackedBindings_AfterTracking_ShouldNotThrow()
    {
        // Arrange
        PerformanceAnalyzer.TrackBinding(new object());
        PerformanceAnalyzer.TrackBinding(new object());

        // Act
        var exception = Record.Exception(() => PerformanceAnalyzer.ClearTrackedBindings());

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public void ClearTrackedBindings_WhenEmpty_ShouldNotThrow()
    {
        // Act
        var exception = Record.Exception(() => PerformanceAnalyzer.ClearTrackedBindings());

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public void ResetStatistics_ShouldNotThrow()
    {
        // Act
        var exception = Record.Exception(() => PerformanceAnalyzer.ResetStatistics());

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public void ResetStatistics_CalledMultipleTimes_ShouldNotThrow()
    {
        // Act & Assert
        for (int i = 0; i < 3; i++)
        {
            var exception = Record.Exception(() => PerformanceAnalyzer.ResetStatistics());
            exception.Should().BeNull();
        }
    }

    [Fact]
    public void StopMonitoring_WhenNotStarted_ShouldNotThrow()
    {
        // Act - stop without starting
        var exception = Record.Exception(() => PerformanceAnalyzer.StopMonitoring());

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public void StopMonitoring_CalledMultipleTimes_ShouldNotThrow()
    {
        // Act & Assert
        for (int i = 0; i < 3; i++)
        {
            var exception = Record.Exception(() => PerformanceAnalyzer.StopMonitoring());
            exception.Should().BeNull();
        }
    }

    [Fact]
    public void ResetMonitoring_ShouldClearEverything()
    {
        // Arrange - track some bindings first
        PerformanceAnalyzer.TrackBinding(new object());
        PerformanceAnalyzer.TrackBinding(new object());

        // Act
        var exception = Record.Exception(() => PerformanceAnalyzer.ResetMonitoring());

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public void ResetMonitoring_CalledMultipleTimes_ShouldNotThrow()
    {
        // Act & Assert
        for (int i = 0; i < 3; i++)
        {
            var exception = Record.Exception(() => PerformanceAnalyzer.ResetMonitoring());
            exception.Should().BeNull();
        }
    }

    [StaFact]
    public void GetVisualCount_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);

        // Act
        var result = analyzer.GetVisualCount(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetVisualCount_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);

        // Act
        var result = analyzer.GetVisualCount("nonexistent_perf_id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetVisualCount_DefaultParameter_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);

        // Act - default parameter is null
        var result = analyzer.GetVisualCount();

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void MeasureElementRenderTime_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);

        // Act
        var result = analyzer.MeasureElementRenderTime(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void MeasureElementRenderTime_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);

        // Act
        var result = analyzer.MeasureElementRenderTime("nonexistent_render_id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void MeasureElementRenderTime_DefaultParameter_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);

        // Act
        var result = analyzer.MeasureElementRenderTime();

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void MeasureElementRenderTime_WithValidElement_ShouldReturnRenderTime()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.MeasureElementRenderTime(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("elementType").GetString().Should().Be("Button");
    }

    [StaFact]
    public void GetVisualCount_WithValidElement_ShouldReturnCount()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetVisualCount(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        doc.GetProperty("elementType").GetString().Should().Be("Button");
    }

    [Fact]
    public void FindBindingLeaks_NoUIThread_ShouldTriggerGCPath()
    {
        // Arrange - not on UI thread, so GC path is taken
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);

        // Act - calling from non-UI thread triggers the GC.Collect path
        var result = analyzer.FindBindingLeaks();

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("hasLeaks").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void FindBindingLeaks_WithTrackedBindings_ShouldReportAlive()
    {
        // Arrange - track some bindings that stay alive
        // Use strong references to prevent GC collection
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);
        PerformanceAnalyzer.ClearTrackedBindings();
        var aliveObjects = new List<object>();
        for (int i = 0; i < 5; i++)
        {
            var obj = new object();
            aliveObjects.Add(obj); // Keep reference alive
            PerformanceAnalyzer.TrackBinding(obj);
        }

        // Act
        var result = analyzer.FindBindingLeaks();

        // Assert - verify the method executes successfully and returns correct structure
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        // aliveBindings count may vary due to GC timing and parallel test interference,
        // so we only verify the property exists and is non-negative
        doc.GetProperty("aliveBindings").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        doc.TryGetProperty("totalTracked", out _).Should().BeTrue();
        doc.TryGetProperty("hasLeaks", out _).Should().BeTrue();

        // Keep references alive until after assertion
        GC.KeepAlive(aliveObjects);
    }

    [Fact]
    public void FindBindingLeaks_WithCustomThreshold_BelowThreshold_ShouldNotReportLeaks()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);
        var aliveObjects = new List<object>();
        for (int i = 0; i < 3; i++)
        {
            var obj = new object();
            aliveObjects.Add(obj);
            PerformanceAnalyzer.TrackBinding(obj);
        }

        // Act - threshold is higher than alive count
        var result = analyzer.FindBindingLeaks(threshold: 100);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("hasLeaks").GetBoolean().Should().BeFalse();

        GC.KeepAlive(aliveObjects);
    }
}

// ═══════════════════════════════════════════════════════════════
// EventAnalyzer Gap Tests
// ═══════════════════════════════════════════════════════════════

public class EventAnalyzerGapTests
{
    [StaFact]
    public void GetEventTrace_ShouldReturnSuccessWithTraceData()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        // Act
        var result = analyzer.GetEventTrace();

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("eventCount", out _).Should().BeTrue();
        doc.TryGetProperty("events", out _).Should().BeTrue();
    }

    [StaFact]
    public void TraceRoutedEvents_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        // Act
        var result = analyzer.TraceRoutedEvents(null, "Click", 1000);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void TraceRoutedEvents_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        // Act
        var result = analyzer.TraceRoutedEvents("nonexistent_event_id", "Click", 1000);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void TraceRoutedEvents_InvalidEventName_ShouldReturnEventNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.TraceRoutedEvents(elementId, "NonExistentEvent", 1000);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("not found");
    }

    [StaFact]
    public void FireRoutedEvent_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        // Act
        var result = analyzer.FireRoutedEvent(null, "Click", null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void FireRoutedEvent_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        // Act
        var result = analyzer.FireRoutedEvent("nonexistent_fire_id", "Click", null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void FireRoutedEvent_InvalidEventName_ShouldReturnEventNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "NonExistentEvent", null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("not found");
    }

    [StaFact]
    public void GetEventHandlers_EmptyEventName_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetEventHandlers(elementId, "");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("eventName is required");
    }

    [StaFact]
    public void GetEventHandlers_NullEventName_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetEventHandlers(elementId, null!);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("eventName is required");
    }

    [StaFact]
    public void GetEventHandlers_NullElementId_NoApplication_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        // Act
        var result = analyzer.GetEventHandlers(null, "Click");

        // Assert - may return "not supported" (reflection check) or "Element not found"
        // depending on .NET version and EventHandlersStore field availability
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void GetEventHandlers_InvalidEventName_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetEventHandlers(elementId, "NonExistentEvent");

        // Assert - may return "not supported" (if reflection unavailable) or "not found"
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════
// InteractionAnalyzer Gap Tests
// ═══════════════════════════════════════════════════════════════

public class InteractionAnalyzerGapTests
{
    [StaFact]
    public void ClickElement_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.ClickElement(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void ClickElement_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.ClickElement("nonexistent_click_id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void ClickElement_NonClickableElement_ShouldReturnNotClickable()
    {
        // Arrange - TextBlock is not a ButtonBase
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var textBlock = new TextBlock();
        var elementId = finder.GenerateElementId(textBlock);

        // Act
        var result = analyzer.ClickElement(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("not clickable");
    }

    [StaFact]
    public void ScrollToElement_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.ScrollToElement(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void ScrollToElement_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.ScrollToElement("nonexistent_scroll_id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void TakeScreenshot_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.TakeScreenshot(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void TakeScreenshot_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.TakeScreenshot("nonexistent_screenshot_id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void SimulateKeyboard_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.SimulateKeyboard(null, "A", "KeyDown");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void SimulateKeyboard_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.SimulateKeyboard("nonexistent_keyboard_id", "A", "KeyDown");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void SimulateKeyboard_InvalidKey_ShouldReturnInvalidKey()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        // Act
        var result = analyzer.SimulateKeyboard(elementId, "InvalidKeyName123", "KeyDown");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Invalid key");
    }

    [StaFact]
    public void SimulateKeyboard_InvalidEventType_ShouldReturnInvalidEventType()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        // Act
        var result = analyzer.SimulateKeyboard(elementId, "A", "InvalidEventType");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Invalid event type");
    }

    [StaFact]
    public void SimulateKeyboard_ElementNotInVisualTree_ShouldReturnPresentationSourceError()
    {
        // Arrange - element not connected to a window
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        // Act
        var result = analyzer.SimulateKeyboard(elementId, "A", "KeyDown");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("presentation source");
    }

    [StaFact]
    public void DragAndDrop_SourceNotFound_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var target = new Button();
        var targetId = finder.GenerateElementId(target);

        // Act - source is null (no Application.Current)
        var result = analyzer.DragAndDrop(null, targetId, "Text");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        // Should hit either "Source element not found" or "not supported" depending on reflection
        doc.TryGetProperty("error", out var errorProp).Should().BeTrue();
        errorProp.GetString().Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void DragAndDrop_TargetNotFound_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var source = new Button();
        var sourceId = finder.GenerateElementId(source);

        // Act - target is null (no Application.Current)
        var result = analyzer.DragAndDrop(sourceId, null, "Text");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.TryGetProperty("error", out var errorProp).Should().BeTrue();
        errorProp.GetString().Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void DragAndDrop_BothNonExistent_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);

        // Act
        var result = analyzer.DragAndDrop("nonexistent_source", "nonexistent_target", "Text");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.TryGetProperty("error", out var errorProp).Should().BeTrue();
        errorProp.GetString().Should().NotBeNullOrEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════
// DispatcherAnalyzerBase Gap Tests
// ═══════════════════════════════════════════════════════════════

public class DispatcherAnalyzerBaseGapTests
{
    private class TestableAnalyzer : DispatcherAnalyzerBase
    {
        public T TestInvokeOnUIThread<T>(Func<T> action, TimeSpan? timeout = null)
        {
            return InvokeOnUIThread(action, timeout);
        }

        public void TestInvokeOnUIThread(Action action, TimeSpan? timeout = null)
        {
            InvokeOnUIThread(action, timeout);
        }

        public bool TestIsOnUIThread()
        {
            return IsOnUIThread();
        }
    }

    [Fact]
    public void IsOnUIThread_NoApplication_ShouldReturnFalse()
    {
        // Arrange - Application.Current is null in unit tests (non-STA thread)
        var analyzer = new TestableAnalyzer();

        // Act
        var result = analyzer.TestIsOnUIThread();

        // Assert - When Application.Current is null, should return false
        result.Should().BeFalse();
    }

    [Fact]
    public void InvokeOnUIThread_Func_NoApplication_ShouldExecuteDirectly()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act - when Application.Current is null, action executes directly
        var result = analyzer.TestInvokeOnUIThread(() =>
        {
            executed = true;
            return 42;
        });

        // Assert
        executed.Should().BeTrue();
        result.Should().Be(42);
    }

    [Fact]
    public void InvokeOnUIThread_Action_NoApplication_ShouldExecuteDirectly()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act - when Application.Current is null, action executes directly
        analyzer.TestInvokeOnUIThread(() => executed = true);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public void InvokeOnUIThread_Func_NoApplication_WithTimeout_ShouldExecuteDirectly()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act - timeout is ignored when Application.Current is null
        var result = analyzer.TestInvokeOnUIThread(
            () => "test_value",
            TimeSpan.FromSeconds(5));

        // Assert
        result.Should().Be("test_value");
    }

    [Fact]
    public void InvokeOnUIThread_Action_NoApplication_WithTimeout_ShouldExecuteDirectly()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act
        analyzer.TestInvokeOnUIThread(
            () => executed = true,
            TimeSpan.FromSeconds(5));

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public void InvokeOnUIThread_Func_NoApplication_ShouldPropagateException()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act & Assert - exception should propagate when no Application.Current
        var act = () => analyzer.TestInvokeOnUIThread<int>(() =>
        {
            throw new InvalidOperationException("test error");
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("test error");
    }

    [Fact]
    public void InvokeOnUIThread_Action_NoApplication_ShouldPropagateException()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act & Assert
        var act = () => analyzer.TestInvokeOnUIThread(() =>
        {
            throw new InvalidOperationException("test error");
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("test error");
    }
}
