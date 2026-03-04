using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

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
