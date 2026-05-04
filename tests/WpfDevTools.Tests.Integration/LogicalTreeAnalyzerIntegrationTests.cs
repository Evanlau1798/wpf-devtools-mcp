using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for LogicalTreeAnalyzer requiring a real WPF Application context.
/// </summary>
[Collection("WpfAndBootstrapIntegration")]
public sealed class LogicalTreeAnalyzerIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private Window? _activeWindow;

    public LogicalTreeAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        _fixture.RunOnUIThread(() =>
        {
            if (_activeWindow == null)
            {
                return;
            }

            _activeWindow.Close();
            _activeWindow = null;

            if (Application.Current != null)
            {
                Application.Current.MainWindow = _previousMainWindow;
            }

            _previousMainWindow = null;
        });
    }

    [Fact]
    public void GetLogicalTree_FromNonUiThread_WithRootElement_ShouldReturnLogicalStructure()
    {
        using var elementFinder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(elementFinder);

        _fixture.RunOnUIThread(() =>
        {
            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button 1" });
            stackPanel.Children.Add(new TextBox { Text = "TextBox 1" });
            stackPanel.Children.Add(new Button { Content = "Button 2" });

            CreateVisibleMainWindow(stackPanel);
        });

        var result = JsonSerializer.SerializeToElement(analyzer.GetLogicalTree(depth: 8, elementId: null));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var tree = result.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("Window");
        tree.GetProperty("childCount").GetInt32().Should().Be(1);

        var contentStackPanel = GetOnlyChildOfType(tree, "StackPanel");
        contentStackPanel.GetProperty("childCount").GetInt32().Should().Be(3);
        GetImmediateChildTypes(contentStackPanel).Should().Equal("Button", "TextBox", "Button");
    }

    [Fact]
    public void GetLogicalTree_WithDepthLimit_ShouldRespectDepthAndCacheParity()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new LogicalTreeAnalyzer(elementFinder);

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button 1" });
            stackPanel.Children.Add(new TextBox { Text = "TextBox 1" });

            var border = new Border { Child = stackPanel };
            CreateVisibleMainWindow(border);

            var elementId = elementFinder.GenerateElementId(border);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetLogicalTree(depth: 1, elementId: elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetLogicalTree(depth: 1, elementId: elementId));

            return JsonSerializer.SerializeToElement(new
            {
                cachedResult,
                lookupResult
            });
        });

        var cachedResult = result.GetProperty("cachedResult");
        var lookupResult = result.GetProperty("lookupResult");

        cachedResult.GetRawText().Should().Be(lookupResult.GetRawText());
        lookupResult.GetProperty("success").GetBoolean().Should().BeTrue();
    lookupResult.GetProperty("returnedNodeCount").GetInt32().Should().Be(2);
    lookupResult.GetProperty("omittedNodeCount").GetInt32().Should().Be(0);
    lookupResult.GetProperty("truncated").GetBoolean().Should().BeFalse();
    lookupResult.GetProperty("appliedOptions").GetProperty("depth").GetInt32().Should().Be(1);
    var depthHint = lookupResult.GetProperty("depthSufficiencyHint");
    depthHint.GetProperty("isSufficient").GetBoolean().Should().BeFalse();
    depthHint.GetProperty("reasonCode").GetString().Should().Be("depthLimitReached");
    depthHint.GetProperty("currentDepth").GetInt32().Should().Be(1);
        depthHint.GetProperty("recommendedDepth").GetInt32().Should().BeGreaterThan(
            depthHint.GetProperty("currentDepth").GetInt32());

        var tree = lookupResult.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("Border");
        tree.GetProperty("childCount").GetInt32().Should().Be(1);

        var stackPanel = GetOnlyChildOfType(tree, "StackPanel");
        stackPanel.GetProperty("childCount").GetInt32().Should().Be(2);
        stackPanel.TryGetProperty("children", out _).Should().BeFalse();
    }

    [Fact]
    public void GetLogicalTree_FromNonUiThread_WithElementId_ShouldPreserveCacheParity()
    {
        using var elementFinder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(elementFinder);
        string? elementId = null;

        _fixture.RunOnUIThread(() =>
        {
            var innerStackPanel = new StackPanel();
            innerStackPanel.Children.Add(new Button { Content = "Button 1" });
            innerStackPanel.Children.Add(new TextBox { Text = "TextBox 1" });

            var outerBorder = new Border
            {
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Header" },
                        innerStackPanel
                    }
                }
            };

            CreateVisibleMainWindow(outerBorder);
            elementId = elementFinder.GenerateElementId(innerStackPanel);
        });

        var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetLogicalTree(depth: 2, elementId: elementId));

        _fixture.RunOnUIThread(() => EvictElementCacheEntry(elementFinder, elementId!));

        var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetLogicalTree(depth: 2, elementId: elementId));

        cachedResult.GetRawText().Should().Be(lookupResult.GetRawText());
        lookupResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var tree = lookupResult.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("StackPanel");
        tree.GetProperty("childCount").GetInt32().Should().Be(2);
        GetImmediateChildTypes(tree).Should().Equal("Button", "TextBox");
    }

    private void CreateVisibleMainWindow(UIElement content)
    {
        var application = Application.Current;
        application.Should().NotBeNull();

        _previousMainWindow ??= application!.MainWindow;

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = content
        };

        _activeWindow = window;
        application.MainWindow = window;
        window.Show();
        window.UpdateLayout();
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private static JsonElement GetOnlyChildOfType(JsonElement node, string type)
    {
        return node
            .GetProperty("children")
            .EnumerateArray()
            .Single(child => child.GetProperty("type").GetString() == type);
    }

    private static IReadOnlyList<string?> GetImmediateChildTypes(JsonElement node)
    {
        return node
            .GetProperty("children")
            .EnumerateArray()
            .Select(child => child.GetProperty("type").GetString())
            .ToArray();
    }
}
