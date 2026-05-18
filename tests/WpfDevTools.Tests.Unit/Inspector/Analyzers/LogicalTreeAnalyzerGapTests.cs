using System.Collections;
using Xunit;
using FluentAssertions;
using System.Text.Json;
using System.Windows;
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

    [StaFact]
    public void GetLogicalTree_WithMaxChildrenPerNode_ShouldStopLogicalEnumerationAfterSentinel()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var root = new CountingLogicalElement(Enumerable
            .Range(0, 20)
            .Select(_ => new Button())
            .Cast<DependencyObject>());
        var elementId = finder.GenerateElementId(root);
        var options = TreeTraversalOptions.Create(
            depth: 2,
            compact: false,
            summaryOnly: false,
            maxNodes: 50,
            maxChildrenPerNode: 1);

        // Act
        var result = JsonSerializer.SerializeToElement(analyzer.GetLogicalTreeWithOptions(options, elementId));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        root.YieldedLogicalChildrenCount.Should().BeLessThanOrEqualTo(2);
        var tree = result.GetProperty("tree");
        tree.GetProperty("children").GetArrayLength().Should().Be(1);
        tree.GetProperty("childCountExact").GetBoolean().Should().BeFalse();
        tree.GetProperty("hasMoreChildren").GetBoolean().Should().BeTrue();
        tree.GetProperty("omittedChildCount").GetInt32().Should().Be(1);
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [StaFact]
    public void GetLogicalTree_WithSummaryAndMaxChildrenPerNode_ShouldStopLogicalEnumerationAfterSentinel()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var root = new CountingLogicalElement(Enumerable
            .Range(0, 20)
            .Select(_ => new Button())
            .Cast<DependencyObject>());
        var elementId = finder.GenerateElementId(root);
        var options = TreeTraversalOptions.Create(
            depth: 2,
            compact: false,
            summaryOnly: true,
            maxNodes: 50,
            maxChildrenPerNode: 1);

        // Act
        var result = JsonSerializer.SerializeToElement(analyzer.GetLogicalTreeWithOptions(options, elementId));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        root.YieldedLogicalChildrenCount.Should().BeLessThanOrEqualTo(2);
        var columns = result.GetProperty("columns").EnumerateArray().Select(column => column.GetString()).ToArray();
        columns.Take(6).Should().Equal("elementId", "type", "name", "childCount", "depth", "parentId");
        var childCountExactIndex = Array.IndexOf(columns, "childCountExact");
        var hasMoreChildrenIndex = Array.IndexOf(columns, "hasMoreChildren");
        childCountExactIndex.Should().BeGreaterThanOrEqualTo(0);
        hasMoreChildrenIndex.Should().BeGreaterThanOrEqualTo(0);
        var nodes = result.GetProperty("nodes");
        nodes.GetArrayLength().Should().Be(2);
        var rootRow = nodes[0].EnumerateArray().ToArray();
        rootRow[childCountExactIndex].GetBoolean().Should().BeFalse();
        rootRow[hasMoreChildrenIndex].GetBoolean().Should().BeTrue();
        result.GetProperty("omittedNodeCount").GetInt32().Should().Be(1);
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [StaFact]
    public void GetLogicalTree_WithMaxChildrenPerNodeAndNonDependencyObjectTail_ShouldStopAfterSentinel()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var root = new CountingLogicalElement(new object[]
        {
            new Button(),
            "omitted-text-1",
            "omitted-text-2",
            "omitted-text-3"
        });
        var elementId = finder.GenerateElementId(root);
        var options = TreeTraversalOptions.Create(
            depth: 2,
            compact: false,
            summaryOnly: false,
            maxNodes: 50,
            maxChildrenPerNode: 1);

        // Act
        var result = JsonSerializer.SerializeToElement(analyzer.GetLogicalTreeWithOptions(options, elementId));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        root.YieldedLogicalChildrenCount.Should().Be(2);
        var tree = result.GetProperty("tree");
        tree.GetProperty("children").GetArrayLength().Should().Be(1);
        tree.GetProperty("childCountExact").GetBoolean().Should().BeFalse();
        tree.GetProperty("hasMoreChildren").GetBoolean().Should().BeTrue();
        tree.GetProperty("omittedChildCount").GetInt32().Should().Be(1);
    }

    [StaFact]
    public void GetLogicalTree_WithDepthLimitAndCappedChildren_ShouldReportLowerBoundTruncation()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var root = new CountingLogicalElement(Enumerable
            .Range(0, 20)
            .Select(_ => new Button())
            .Cast<DependencyObject>());
        var elementId = finder.GenerateElementId(root);
        var options = TreeTraversalOptions.Create(
            depth: 0,
            compact: false,
            summaryOnly: false,
            maxNodes: 50,
            maxChildrenPerNode: 1);

        // Act
        var result = JsonSerializer.SerializeToElement(analyzer.GetLogicalTreeWithOptions(options, elementId));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        root.YieldedLogicalChildrenCount.Should().BeLessThanOrEqualTo(2);
        result.GetProperty("omittedNodeCount").GetInt32().Should().Be(1);
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        var tree = result.GetProperty("tree");
        tree.GetProperty("childCountExact").GetBoolean().Should().BeFalse();
        tree.GetProperty("hasMoreChildren").GetBoolean().Should().BeTrue();
        tree.GetProperty("omittedChildCount").GetInt32().Should().Be(1);
    }

    [StaFact]
    public void GetLogicalTree_WithMaxChildrenPerNodeAndNonDependencyObjectPrefix_ShouldStopAtRawScanSentinel()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);
        var root = new CountingLogicalElement(new object[]
        {
            "text-1",
            "text-2",
            "text-3",
            new Button()
        });
        var elementId = finder.GenerateElementId(root);
        var options = TreeTraversalOptions.Create(
            depth: 2,
            compact: false,
            summaryOnly: false,
            maxNodes: 50,
            maxChildrenPerNode: 1);

        // Act
        var result = JsonSerializer.SerializeToElement(analyzer.GetLogicalTreeWithOptions(options, elementId));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        root.YieldedLogicalChildrenCount.Should().BeLessThanOrEqualTo(3);
        result.GetProperty("omittedNodeCount").GetInt32().Should().Be(1);
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        var tree = result.GetProperty("tree");
        tree.GetProperty("childCountExact").GetBoolean().Should().BeFalse();
        tree.GetProperty("hasMoreChildren").GetBoolean().Should().BeTrue();
        tree.GetProperty("omittedChildCount").GetInt32().Should().Be(1);
    }

    private sealed class CountingLogicalElement : FrameworkElement
    {
        public CountingLogicalElement(IEnumerable<DependencyObject> children)
        {
            Children = children.Cast<object>().ToArray();
        }

        public CountingLogicalElement(IEnumerable<object> children)
        {
            Children = children.ToArray();
        }

        private object[] Children { get; }

        public int YieldedLogicalChildrenCount { get; private set; }

        protected override IEnumerator LogicalChildren
        {
            get
            {
                foreach (var child in Children)
                {
                    YieldedLogicalChildrenCount++;
                    yield return child;
                }
            }
        }
    }
}
