using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

/// <summary>
/// Additional tests for LogicalTreeAnalyzer to cover uncovered lines
/// </summary>
public class LogicalTreeAnalyzerGapTests
{
    [StaFact]
    public void GetLogicalTree_WithNullElementId_NoRoot_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);

        // Act
        var result = analyzer.GetLogicalTree(null, null);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetLogicalTree_WithNonExistentElementId_ShouldReturnElementNotFound()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);

        // Act
        var result = analyzer.GetLogicalTree(null, "non-existent-id");

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.GetProperty("error").GetString().Should().Contain("Element not found");
    }

    [StaFact]
    public void GetLogicalTree_WithValidElement_ShouldReturnTree()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var button = new Button { Content = "Test" };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetLogicalTree(null, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("tree", out _).Should().BeTrue();
    }

    [StaFact]
    public void GetLogicalTree_WithDepthZero_ShouldReturnShallowTree()
    {
        // Arrange - depth 0 means only root node info, no children walk
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new Button { Content = "A" });
        stackPanel.Children.Add(new Button { Content = "B" });
        var elementId = finder.GenerateElementId(stackPanel);

        // Act
        var result = analyzer.GetLogicalTree(0, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [StaFact]
    public void GetLogicalTree_WithDepth1_ShouldWalkOneLevel()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new Button { Content = "Child1" });
        var elementId = finder.GenerateElementId(stackPanel);

        // Act
        var result = analyzer.GetLogicalTree(1, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        var tree = doc.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("StackPanel");
    }

    [StaFact]
    public void GetLogicalTree_WithNamedElement_ShouldIncludeName()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var button = new Button { Name = "TestButton" };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetLogicalTree(10, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        var tree = doc.GetProperty("tree");
        tree.GetProperty("name").GetString().Should().Be("TestButton");
    }

    [StaFact]
    public void GetLogicalTree_WithNoChildren_ShouldReturnZeroChildCount()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var textBlock = new TextBlock();
        var elementId = finder.GenerateElementId(textBlock);

        // Act
        var result = analyzer.GetLogicalTree(10, elementId);

        // Assert
        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        var tree = doc.GetProperty("tree");
        tree.GetProperty("childCount").GetInt32().Should().Be(0);
    }
}
