using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// E2E tests for MCP tree inspection tools (get_visual_tree, get_logical_tree, get_windows).
/// Validates Visual/Logical tree traversal through the full MCP protocol pipeline.
///
/// Response schema notes:
/// - Tree tools return { success, tree: { elementId, type, name, childCount, children } }
/// - get_windows returns { success, windowCount, windows: [{ index, title, type, isActive, isVisible, isMainWindow, elementId }] }
/// </summary>
[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class TreeInspectionE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TreeInspectionE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GetVisualTree_WithDepth1_ShouldReturnRootAndImmediateChildren()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_visual_tree",
            new { processId = _fixture.TestAppProcessId, depth = 1 });

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        result.TryGetProperty("tree", out var tree).Should().BeTrue(
            "visual tree response should contain a 'tree' property");

        tree.TryGetProperty("elementId", out var elementId).Should().BeTrue(
            "root tree node should have an elementId");
        tree.GetProperty("type").GetString().Should().NotBeNullOrEmpty(
            "root tree node should have a type name");

        _output.WriteLine($"Root: elementId={elementId}, type={tree.GetProperty("type")}");
    }

    [Fact]
    public async Task GetVisualTree_WithDepth3_ShouldReturnNestedElements()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_visual_tree",
            new { processId = _fixture.TestAppProcessId, depth = 3 });

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        var tree = result.GetProperty("tree");

        if (tree.TryGetProperty("children", out var children))
        {
            children.GetArrayLength().Should().BeGreaterOrEqualTo(1,
                "root window should have child elements at depth 3");

            _output.WriteLine($"Root has {children.GetArrayLength()} children at depth 3");
        }
    }

    [Fact]
    public async Task GetLogicalTree_ShouldReturnApplicationContent()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_logical_tree",
            new { processId = _fixture.TestAppProcessId, depth = 3 });

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        result.TryGetProperty("tree", out var tree).Should().BeTrue(
            "logical tree response should contain a 'tree' property");

        tree.TryGetProperty("type", out var typeName).Should().BeTrue(
            "logical tree root should have a type");

        _output.WriteLine($"Logical tree root type: {typeName}");
    }

    [Fact]
    public async Task GetWindows_ShouldReturnAtLeastMainWindow()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_windows",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"get_windows result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        result.TryGetProperty("windowCount", out var windowCount).Should().BeTrue(
            "get_windows should report windowCount");
        windowCount.GetInt32().Should().BeGreaterOrEqualTo(1,
            "at least the main window should be reported");

        result.TryGetProperty("windows", out var windows).Should().BeTrue(
            "get_windows should return a windows array");
        windows.GetArrayLength().Should().BeGreaterOrEqualTo(1);

        var mainWindow = windows[0];
        mainWindow.TryGetProperty("elementId", out var windowElementId).Should().BeTrue(
            "each window should have an elementId for subsequent tool calls");
        windowElementId.GetString().Should().NotBeNullOrEmpty();
        mainWindow.TryGetProperty("title", out _).Should().BeTrue(
            "each window should have a title");
        mainWindow.TryGetProperty("isMainWindow", out _).Should().BeTrue(
            "window snapshots should identify the app main window explicitly");
        mainWindow.TryGetProperty("isVisible", out _).Should().BeTrue(
            "window snapshots should include visibility to explain focus timing");
    }

    [Fact]
    public async Task GetVisualTree_WithElementId_ShouldReturnSubtree()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        // First, get the root tree to find a child elementId
        var rootResult = await _fixture.Client.CallToolAsync(
            "get_visual_tree",
            new { processId = _fixture.TestAppProcessId, depth = 2 });

        rootResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var rootTree = rootResult.GetProperty("tree");
        var childElementId = FindFirstChildElementId(rootTree);

        childElementId.Should().NotBeNull(
            "root tree at depth 2 should have at least one child with an elementId");

        // Query the subtree using discovered elementId
        var subtreeResult = await _fixture.Client.CallToolAsync(
            "get_visual_tree",
            new { processId = _fixture.TestAppProcessId, elementId = childElementId, depth = 2 });

        subtreeResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var subtree = subtreeResult.GetProperty("tree");
        subtree.GetProperty("elementId").GetString().Should().Be(childElementId);

        _output.WriteLine($"Subtree for {childElementId}: type={subtree.GetProperty("type")}");
    }

    [Fact]
    public async Task GetVisualCount_ShouldReturnPositiveCount()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_visual_count",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"Visual count: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        var count = result.GetProperty("count").GetInt32();
        count.Should().BeGreaterThan(10,
            "TestApp should have a substantial visual tree (TabControl + content)");
    }

    private static string? FindFirstChildElementId(JsonElement treeNode)
    {
        if (treeNode.TryGetProperty("children", out var children) &&
            children.ValueKind == JsonValueKind.Array &&
            children.GetArrayLength() > 0)
        {
            var firstChild = children[0];
            if (firstChild.TryGetProperty("elementId", out var elementId))
                return elementId.GetString();
        }

        return null;
    }
}
