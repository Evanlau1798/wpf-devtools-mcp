using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public class TreeCompressionHandlerTests
{
    [StaFact]
    public async Task GetLogicalTree_WithCompactTrue_ShouldOmitNullFieldsOnLeafNode()
    {
        var finder = new ElementFinder();
        var textBlock = new TextBlock();
        var elementId = finder.GenerateElementId(textBlock);
        var handler = CreateHandler(finder);
        var parameters = ToJsonElement(new { elementId, compact = true, depth = 1 });

        var result = await handler.HandleAsync("get_logical_tree", parameters, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var tree = json.GetProperty("tree");
        tree.TryGetProperty("name", out _).Should().BeFalse();
        tree.TryGetProperty("children", out _).Should().BeFalse();
    }

    [StaFact]
    public async Task GetLogicalTree_WithSummaryOnly_ShouldReturnFlatSummaryFormat()
    {
        var finder = new ElementFinder();
        var root = new StackPanel();
        root.Children.Add(new Button { Name = "First" });
        root.Children.Add(new Button { Name = "Second" });
        var elementId = finder.GenerateElementId(root);
        var handler = CreateHandler(finder);
        var parameters = ToJsonElement(new { elementId, summaryOnly = true, depth = 2 });

        var result = await handler.HandleAsync("get_logical_tree", parameters, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("format").GetString().Should().Be("flat-summary-v1");
        json.GetProperty("columns").GetArrayLength().Should().BeGreaterThan(0);
        json.GetProperty("nodes").GetArrayLength().Should().Be(3);
        json.TryGetProperty("tree", out _).Should().BeFalse();
    }

    [StaFact]
    public async Task GetLogicalTree_WithMaxNodes_ShouldTruncateAndReportCounts()
    {
        var finder = new ElementFinder();
        var root = new StackPanel();
        root.Children.Add(new Button { Name = "First" });
        root.Children.Add(new Button { Name = "Second" });
        var elementId = finder.GenerateElementId(root);
        var handler = CreateHandler(finder);
        var parameters = ToJsonElement(new { elementId, maxNodes = 2, depth = 5 });

        var result = await handler.HandleAsync("get_logical_tree", parameters, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("truncated").GetBoolean().Should().BeTrue();
        json.GetProperty("returnedNodeCount").GetInt32().Should().Be(2);
        json.GetProperty("omittedNodeCount").GetInt32().Should().BeGreaterThan(0);
    }

    [StaFact]
    public async Task GetVisualTree_WithMaxChildrenPerNode_ShouldLimitExpandedChildren()
    {
        var finder = new ElementFinder();
        var root = new StackPanel();
        root.Children.Add(new Button { Name = "First" });
        root.Children.Add(new Button { Name = "Second" });
        root.Children.Add(new Button { Name = "Third" });
        var elementId = finder.GenerateElementId(root);
        var handler = CreateHandler(finder);
        var parameters = ToJsonElement(new { elementId, maxChildrenPerNode = 1, depth = 2 });

        var result = await handler.HandleAsync("get_visual_tree", parameters, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var tree = json.GetProperty("tree");
        tree.GetProperty("children").GetArrayLength().Should().Be(1);
        tree.GetProperty("omittedChildCount").GetInt32().Should().Be(2);
        json.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    private static TreeHandlers CreateHandler(ElementFinder finder)
    {
        return new TreeHandlers(
            new VisualTreeAnalyzer(finder),
            new LogicalTreeAnalyzer(finder),
            new XamlSerializer(),
            finder);
    }
}
