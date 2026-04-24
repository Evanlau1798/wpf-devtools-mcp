using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for tree analyzers using TestApp golden sample scenarios.
/// Tests deep nesting (Tab 2), large visual trees (Tab 8 performance),
/// and logical tree traversal.
/// </summary>
[Collection("WpfIntegration")]
public class TestAppTreeIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private MainWindow? _activeTestAppWindow;

    public TestAppTreeIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        _fixture.RunOnUIThread(() =>
        {
            if (_activeTestAppWindow == null)
            {
                return;
            }

            _activeTestAppWindow.Close();
            _activeTestAppWindow = null;

            if (Application.Current != null)
            {
                Application.Current.MainWindow = _previousMainWindow;
            }

            _previousMainWindow = null;
        });
    }

    [Fact]
    public void GetVisualTree_WithDeepNesting_ShouldTraverseAllLevels()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectTab(context.MainTabControl, context.NestedTreeTab, context.Window);

            var rootId = elementFinder.GenerateElementId(context.NestedRootBorder);

            var cachedResult = analyzer.GetVisualTree(maxDepth: 10, elementId: rootId);
            EvictElementCacheEntry(elementFinder, rootId);
            var lookupResult = analyzer.GetVisualTree(maxDepth: 10, elementId: rootId);

            return JsonSerializer.SerializeToElement(new
            {
                cachedResult,
                lookupResult
            });
        });

        var cachedResult = result.GetProperty("cachedResult");
        var lookupResult = result.GetProperty("lookupResult");

        cachedResult.GetProperty("success").GetBoolean().Should().BeTrue();
        lookupResult.GetProperty("success").GetBoolean().Should().BeTrue();
        cachedResult.GetProperty("returnedNodeCount").GetInt32().Should().Be(
            lookupResult.GetProperty("returnedNodeCount").GetInt32());
        cachedResult.GetProperty("tree").GetRawText().Should().Be(lookupResult.GetProperty("tree").GetRawText());
        lookupResult.GetProperty("returnedNodeCount").GetInt32().Should().BeGreaterThan(10);

        var tree = lookupResult.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("Border");

        var outerStackPanel = GetOnlyChildOfType(tree, "StackPanel");
        GetChildrenOfType(outerStackPanel, "TextBlock").Should().ContainSingle();

        var greenBorder = GetOnlyChildOfType(outerStackPanel, "Border");
        var secondLevelStackPanel = GetOnlyChildOfType(greenBorder, "StackPanel");
        var orangeBorder = GetOnlyChildOfType(secondLevelStackPanel, "Border");
        var thirdLevelStackPanel = GetOnlyChildOfType(orangeBorder, "StackPanel");
        var redBorder = GetOnlyChildOfType(thirdLevelStackPanel, "Border");
        var deepestStackPanel = GetOnlyChildOfType(redBorder, "StackPanel");
        var deepestChildTypes = GetChildTypes(deepestStackPanel);

        deepestChildTypes.Should().Contain("TextBlock");
        deepestChildTypes.Should().Contain("Button");
        deepestChildTypes.Should().Contain("TextBox");
    }

    [Fact]
    public void GetVisualTree_WithDepthLimit_ShouldNotExceedLimit()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectTab(context.MainTabControl, context.NestedTreeTab, context.Window);

            var rootId = elementFinder.GenerateElementId(context.NestedRootBorder);
            EvictElementCacheEntry(elementFinder, rootId);

            return JsonSerializer.SerializeToElement(analyzer.GetVisualTree(maxDepth: 2, elementId: rootId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        var tree = result.GetProperty("tree");
        var outerStackPanel = GetOnlyChildOfType(tree, "StackPanel");
        var greenBorder = GetOnlyChildOfType(outerStackPanel, "Border");

        greenBorder.GetProperty("childCount").GetInt32().Should().BeGreaterThan(0);
        greenBorder.TryGetProperty("children", out _).Should().BeFalse();
    }

    [Fact]
    public void GetVisualCount_WithLargeTree_ShouldCountAllElements()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new PerformanceAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectTab(context.MainTabControl, context.PerformanceTab, context.Window);
            context.PerformanceStackPanel.UpdateLayout();
            context.Window.UpdateLayout();

            var rootId = elementFinder.GenerateElementId(context.PerformanceStackPanel);
            var headerId = elementFinder.GenerateElementId((UIElement)context.PerformanceStackPanel.Children[0]);
            var firstGeneratedRowId = elementFinder.GenerateElementId((UIElement)context.PerformanceStackPanel.Children[1]);
            var sentinelId = elementFinder.GenerateElementId(context.PerformanceBottomSentinel);

            EvictElementCacheEntry(elementFinder, rootId);
            EvictElementCacheEntry(elementFinder, headerId);
            EvictElementCacheEntry(elementFinder, firstGeneratedRowId);
            EvictElementCacheEntry(elementFinder, sentinelId);

            var stackPanelResult = JsonSerializer.SerializeToElement(analyzer.GetVisualCount(rootId));
            var headerResult = JsonSerializer.SerializeToElement(analyzer.GetVisualCount(headerId));
            var firstGeneratedRowResult = JsonSerializer.SerializeToElement(analyzer.GetVisualCount(firstGeneratedRowId));
            var sentinelResult = JsonSerializer.SerializeToElement(analyzer.GetVisualCount(sentinelId));

            return JsonSerializer.SerializeToElement(new
            {
                stackPanelResult,
                headerResult,
                firstGeneratedRowResult,
                sentinelResult,
                directChildCount = context.PerformanceStackPanel.Children.Count,
                lastChildIsSentinel = ReferenceEquals(
                    context.PerformanceStackPanel.Children[context.PerformanceStackPanel.Children.Count - 1],
                    context.PerformanceBottomSentinel)
            });
        });

        result.GetProperty("directChildCount").GetInt32().Should().Be(102);
        result.GetProperty("lastChildIsSentinel").GetBoolean().Should().BeTrue();

    var stackPanelResult = result.GetProperty("stackPanelResult");
    var headerResult = result.GetProperty("headerResult");
    var firstGeneratedRowResult = result.GetProperty("firstGeneratedRowResult");
    var sentinelResult = result.GetProperty("sentinelResult");

    stackPanelResult.GetProperty("success").GetBoolean().Should().BeTrue();
    stackPanelResult.GetProperty("elementType").GetString().Should().Be("StackPanel");
    headerResult.GetProperty("success").GetBoolean().Should().BeTrue();
    firstGeneratedRowResult.GetProperty("success").GetBoolean().Should().BeTrue();
    sentinelResult.GetProperty("success").GetBoolean().Should().BeTrue();

    var totalVisualCount = stackPanelResult.GetProperty("count").GetInt32();
    var headerVisualCount = headerResult.GetProperty("count").GetInt32();
    var firstGeneratedRowVisualCount = firstGeneratedRowResult.GetProperty("count").GetInt32();
    var sentinelVisualCount = sentinelResult.GetProperty("count").GetInt32();
    var expectedVisualCount = 1 + headerVisualCount + (100 * firstGeneratedRowVisualCount) + sentinelVisualCount;

    totalVisualCount.Should().Be(expectedVisualCount);
    stackPanelResult.GetProperty("totalCount").GetInt32().Should().Be(totalVisualCount);
    }

    [Fact]
    public void CompareTree_WithNestedBorders_ShouldExecuteSuccessfully()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectTab(context.MainTabControl, context.NestedTreeTab, context.Window);

            var stackPanelId = elementFinder.GenerateElementId(context.NestedOuterStackPanel);
            EvictElementCacheEntry(elementFinder, stackPanelId);

            return JsonSerializer.SerializeToElement(analyzer.CompareTree(stackPanelId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("visualChildCount").GetInt32().Should().Be(2);
        result.GetProperty("logicalChildCount").GetInt32().Should().Be(2);
        result.GetProperty("differenceCount").GetInt32().Should().Be(0);
        result.GetProperty("differences").GetArrayLength().Should().Be(0);
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private static void SelectTab(TabControl mainTabControl, TabItem tabItem, MainWindow window)
    {
        mainTabControl.SelectedItem = tabItem;
        window.UpdateLayout();
    }

    private TestAppTreeWindowContext CreateRealTestAppWindow()
    {
        var application = Application.Current;
        application.Should().NotBeNull();

        _previousMainWindow ??= application!.MainWindow;

        var window = new MainWindow();
        _activeTestAppWindow = window;
        application.MainWindow = window;
        window.Show();
        window.UpdateLayout();

        var mainTabControl = window.FindName("MainTabControl") as TabControl;
        var performanceTab = window.FindName("PerformanceTab") as TabItem;
        var performanceStackPanel = window.FindName("PerformanceStackPanel") as StackPanel;
        var performanceBottomSentinel = window.FindName("PerformanceBottomSentinel") as Border;

        mainTabControl.Should().NotBeNull();
        performanceTab.Should().NotBeNull();
        performanceStackPanel.Should().NotBeNull();
        performanceBottomSentinel.Should().NotBeNull();

        var nestedTreeTab = mainTabControl!.Items
            .OfType<TabItem>()
            .SingleOrDefault(tabItem => Equals(tabItem.Header, "Nested Tree"));
        nestedTreeTab.Should().NotBeNull();

        var nestedScrollViewer = nestedTreeTab!.Content as ScrollViewer;
        nestedScrollViewer.Should().NotBeNull();

        var nestedRootBorder = nestedScrollViewer!.Content as Border;
        nestedRootBorder.Should().NotBeNull();

        var nestedOuterStackPanel = nestedRootBorder!.Child as StackPanel;
        nestedOuterStackPanel.Should().NotBeNull();

        return new TestAppTreeWindowContext(
            window,
            mainTabControl,
            nestedTreeTab,
            nestedRootBorder,
            nestedOuterStackPanel!,
            performanceTab!,
            performanceStackPanel!,
            performanceBottomSentinel!);
    }

    private static JsonElement GetOnlyChildOfType(JsonElement node, string type)
    {
        var matches = GetChildrenOfType(node, type);
        matches.Should().ContainSingle();
        return matches[0];
    }

    private static JsonElement[] GetChildrenOfType(JsonElement node, string type)
    {
        return node.GetProperty("children")
            .EnumerateArray()
            .Where(child => child.GetProperty("type").GetString() == type)
            .ToArray();
    }

    private static string[] GetChildTypes(JsonElement node)
    {
        return node.GetProperty("children")
            .EnumerateArray()
            .Select(child => child.GetProperty("type").GetString())
            .Where(type => type != null)
            .Cast<string>()
            .ToArray();
    }

    private sealed record TestAppTreeWindowContext(
        MainWindow Window,
        TabControl MainTabControl,
        TabItem NestedTreeTab,
        Border NestedRootBorder,
        StackPanel NestedOuterStackPanel,
        TabItem PerformanceTab,
        StackPanel PerformanceStackPanel,
        Border PerformanceBottomSentinel);
}
