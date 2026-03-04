using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

/// <summary>
/// Additional tests for VisualTreeAnalyzer to cover uncovered lines
/// </summary>
public class VisualTreeAnalyzerGapTests
{

    [StaFact]
    public void GetVisualTree_WithNullElementId_NoRoot_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);

        // Act
        var result = analyzer.GetVisualTree(null, null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("not found");
    }

    [StaFact]
    public void GetVisualTree_WithNonExistentElementId_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);

        // Act
        var result = analyzer.GetVisualTree(null, "non-existent-id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public void GetVisualTree_WithValidElement_ShouldReturnTree()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var button = new Button { Content = "Test" };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetVisualTree(5, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("tree", out _).Should().BeTrue();
    }

    [StaFact]
    public void GetVisualTree_WithDepthZero_ShouldReturnShallowTree()
    {
        // Arrange - depth 0 triggers the maxDepth branch
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetVisualTree(0, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [StaFact]
    public void CompareTree_WithNullElementId_NoRoot_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);

        // Act
        var result = analyzer.CompareTree(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("not found");
    }

    [StaFact]
    public void CompareTree_WithNonExistentElementId_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);

        // Act
        var result = analyzer.CompareTree("non-existent-id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public void CompareTree_WithValidElement_ShouldReturnComparison()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new Button());
        var elementId = finder.GenerateElementId(stackPanel);

        // Act
        var result = analyzer.CompareTree(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("visualChildCount", out _).Should().BeTrue();
        doc.TryGetProperty("logicalChildCount", out _).Should().BeTrue();
        doc.TryGetProperty("differenceCount", out _).Should().BeTrue();
    }

    [StaFact]
    public void GetNameScope_WithNullElementId_NoRoot_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);

        // Act
        var result = analyzer.GetNameScope(null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("not found");
    }

    [StaFact]
    public void GetNameScope_WithNonExistentElementId_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);

        // Act
        var result = analyzer.GetNameScope("non-existent");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public void GetNameScope_WithValidElement_ShouldReturnNameScopeInfo()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var button = new Button { Name = "TestButton" };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetNameScope(elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("hasNameScope", out _).Should().BeTrue();
        doc.TryGetProperty("namedElementCount", out _).Should().BeTrue();
    }

    [StaFact]
    public void GetVisualTree_WithNamedElement_ShouldIncludeName()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var button = new Button { Name = "MyButton" };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetVisualTree(10, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        var tree = doc.GetProperty("tree");
        tree.GetProperty("name").GetString().Should().Be("MyButton");
    }

    [StaFact]
    public void GetVisualTree_DefaultDepth_ShouldUse50()
    {
        // Arrange - no depth specified, defaults to 50
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetVisualTree(null, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
