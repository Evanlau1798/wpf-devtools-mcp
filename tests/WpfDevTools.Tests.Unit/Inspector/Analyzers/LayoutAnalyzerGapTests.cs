using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

/// <summary>
/// Gap tests for LayoutAnalyzer covering missing code paths
/// </summary>
public class LayoutAnalyzerGapTests
{

    // ?€?€?€ GetLayoutInfo ?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€

    [StaFact]
    public void GetLayoutInfo_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange - no Application.Current, so root is null
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.GetLayoutInfo(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetLayoutInfo_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.GetLayoutInfo("nonexistent_layout_id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetLayoutInfo_DefaultElementId_ShouldReturnElementNotFound()
    {
        // Arrange - calling without parameter defaults to null elementId
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.GetLayoutInfo();

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    // ?€?€?€ GetClippingInfo ?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€

    [StaFact]
    public void GetClippingInfo_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.GetClippingInfo(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetClippingInfo_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.GetClippingInfo("nonexistent_clip_id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetClippingInfo_DefaultElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.GetClippingInfo();

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetClippingInfo_WithNoClip_ShouldReturnClipInfo()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetClippingInfo(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("hasClip").GetBoolean().Should().BeFalse();
        doc.GetProperty("clipToBounds").GetBoolean().Should().BeFalse();
    }

    // ?€?€?€ InvalidateLayout ?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€

    [StaFact]
    public void InvalidateLayout_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.InvalidateLayout(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void InvalidateLayout_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.InvalidateLayout("nonexistent_invalidate_id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void InvalidateLayout_DefaultElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.InvalidateLayout();

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void InvalidateLayout_WithValidElement_ShouldSucceed()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.InvalidateLayout(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("message").GetString().Should().Contain("invalidated");
    }

    // ?€?€?€ HighlightElement ?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€?€

    [StaFact]
    public void HighlightElement_NullElementId_NoApplication_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.HighlightElement(null, "Red", 1000);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void HighlightElement_NonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);

        // Act
        var result = analyzer.HighlightElement("nonexistent_highlight_id", "Blue", 1000);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }
}
