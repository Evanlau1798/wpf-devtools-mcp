using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

/// <summary>
/// Gap tests for StyleAnalyzer covering missing code paths
/// </summary>
public class StyleAnalyzerGapTests
{
    // ─── GetAppliedStyles ───────────────────────────────────────

    [StaFact]
    public void GetAppliedStyles_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange - no Application.Current, so root is null
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.GetAppliedStyles(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetAppliedStyles_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.GetAppliedStyles("nonexistent_id_123");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetAppliedStyles_ElementWithNoStyle_ShouldReturnEmptyStyles()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button(); // No style set
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetAppliedStyles(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("count").GetInt32().Should().Be(0);
        doc.GetProperty("styles").GetArrayLength().Should().Be(0);
    }

    // ─── GetTriggers ────────────────────────────────────────────

    [StaFact]
    public void GetTriggers_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.GetTriggers(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetTriggers_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.GetTriggers("nonexistent_id_456");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    // ─── GetTemplateTree ────────────────────────────────────────

    [StaFact]
    public void GetTemplateTree_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.GetTemplateTree(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetTemplateTree_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.GetTemplateTree("nonexistent_id_789");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetTemplateTree_ControlWithNullTemplate_ShouldReturnNoTemplate()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var control = new Control(); // Base Control with no template
        control.Template = null;
        var elementId = finder.GenerateElementId(control);

        // Act
        var result = analyzer.GetTemplateTree(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("hasTemplate").GetBoolean().Should().BeFalse();
        doc.GetProperty("message").GetString().Should().Contain("no template");
    }

    // ─── GetResourceChain ───────────────────────────────────────

    [StaFact]
    public void GetResourceChain_EmptyResourceKey_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetResourceChain(elementId, "");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("resourceKey is required");
    }

    [StaFact]
    public void GetResourceChain_NullResourceKey_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetResourceChain(elementId, null!);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("resourceKey is required");
    }

    [StaFact]
    public void GetResourceChain_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.GetResourceChain(null, "SomeKey");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetResourceChain_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.GetResourceChain("nonexistent_id", "SomeKey");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetResourceChain_ResourceNotFound_ShouldReturnEmptyChain()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button(); // No resources defined
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetResourceChain(elementId, "NonExistentResource");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("found").GetBoolean().Should().BeFalse();
        doc.GetProperty("chain").GetArrayLength().Should().Be(0);
    }

    // ─── OverrideStyleSetter ────────────────────────────────────

    [StaFact]
    public void OverrideStyleSetter_EmptyPropertyName_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.OverrideStyleSetter(elementId, "", 100.0);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("propertyName is required");
    }

    [StaFact]
    public void OverrideStyleSetter_NullPropertyName_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.OverrideStyleSetter(elementId, null!, 100.0);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("propertyName is required");
    }

    [StaFact]
    public void OverrideStyleSetter_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.OverrideStyleSetter(null, "Width", 100.0);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void OverrideStyleSetter_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        // Act
        var result = analyzer.OverrideStyleSetter("nonexistent_id", "Width", 100.0);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void OverrideStyleSetter_NonExistentProperty_ShouldReturnPropertyNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.OverrideStyleSetter(elementId, "NonExistentDpProperty", 100.0);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("not found");
    }
}
