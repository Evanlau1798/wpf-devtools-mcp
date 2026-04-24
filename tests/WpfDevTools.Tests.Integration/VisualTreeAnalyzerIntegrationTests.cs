using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for VisualTreeAnalyzer requiring full WPF Application context
/// </summary>
[Collection("WpfIntegration")]
public class VisualTreeAnalyzerIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private Window? _activeWindow;

    public VisualTreeAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        _fixture.RunOnUIThread(() =>
        {
            if (_activeWindow != null)
            {
                _activeWindow.Close();
                _activeWindow = null;

                if (Application.Current != null)
                {
                    Application.Current.MainWindow = _previousMainWindow;
                }

                _previousMainWindow = null;
                return;
            }

            if (Application.Current?.MainWindow is Window mainWindow)
            {
                mainWindow.Content = null;
                mainWindow.UpdateLayout();
            }
        });
    }

    [Fact]
    public void GetVisualTree_WithRootElement_ShouldReturnTree()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button 1" });
            stackPanel.Children.Add(new TextBox { Text = "TextBox 1" });
            stackPanel.Children.Add(new Button { Content = "Button 2" });

            CreateVisibleMainWindow(stackPanel);

            return JsonSerializer.SerializeToElement(analyzer.GetVisualTree(maxDepth: 8, elementId: null));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var tree = result.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("Window");

        var contentStackPanel = FindNodesByType(tree, "StackPanel")
            .Single(node => node.GetProperty("childCount").GetInt32() == 3);
        GetImmediateChildTypes(contentStackPanel).Should().Equal("Button", "TextBox", "Button");
    }

    [Fact]
    public void GetVisualTree_WithDepthLimit_ShouldRespectDepth()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var border1 = new Border();
            var border2 = new Border();
            var border3 = new Border();
            var button = new Button { Content = "Deep Button" };

            border3.Child = button;
            border2.Child = border3;
            border1.Child = border2;

            Application.Current.MainWindow.Content = border1;
            Application.Current.MainWindow.UpdateLayout();

            var elementId = elementFinder.GenerateElementId(border1);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetVisualTree(maxDepth: 2, elementId: elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetVisualTree(maxDepth: 2, elementId: elementId));

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

        var tree = lookupResult.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("Border");

        var middleBorder = GetOnlyChildOfType(tree, "Border");
        var deepestBorder = GetOnlyChildOfType(middleBorder, "Border");
        deepestBorder.GetProperty("childCount").GetInt32().Should().BeGreaterThan(0);
        deepestBorder.TryGetProperty("children", out _).Should().BeFalse();
    }

    [Fact]
    public void GetNameScope_WithNamedElements_ShouldReturnNames()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var stackPanel = new StackPanel();
            NameScope.SetNameScope(stackPanel, new NameScope());

            var button1 = new Button { Content = "Button 1", Name = "TestButton1" };
            var button2 = new Button { Content = "Button 2", Name = "TestButton2" };
            var textBox = new TextBox { Text = "Test", Name = "TestTextBox" };

            stackPanel.Children.Add(button1);
            stackPanel.Children.Add(button2);
            stackPanel.Children.Add(textBox);
            stackPanel.RegisterName(button1.Name, button1);
            stackPanel.RegisterName(button2.Name, button2);
            stackPanel.RegisterName(textBox.Name, textBox);

            Application.Current.MainWindow.Content = stackPanel;
            Application.Current.MainWindow.UpdateLayout();

            var elementId = elementFinder.GenerateElementId(stackPanel);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetNameScope(elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetNameScope(elementId));

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
        lookupResult.GetProperty("hasNameScope").GetBoolean().Should().BeTrue();
        lookupResult.GetProperty("namedElementCount").GetInt32().Should().Be(3);
        lookupResult.GetProperty("namedElements").EnumerateArray().Should().Contain(element =>
            element.GetProperty("name").GetString() == "TestButton1"
            && element.GetProperty("type").GetString() == "Button"
            && !string.IsNullOrWhiteSpace(element.GetProperty("elementId").GetString()));
        lookupResult.GetProperty("namedElements").EnumerateArray().Should().Contain(element =>
            element.GetProperty("name").GetString() == "TestButton2"
            && element.GetProperty("type").GetString() == "Button"
            && !string.IsNullOrWhiteSpace(element.GetProperty("elementId").GetString()));
        lookupResult.GetProperty("namedElements").EnumerateArray().Should().Contain(element =>
            element.GetProperty("name").GetString() == "TestTextBox"
            && element.GetProperty("type").GetString() == "TextBox"
            && !string.IsNullOrWhiteSpace(element.GetProperty("elementId").GetString()));
    }

    [Fact]
    public void CompareTree_ShouldExecuteSuccessfully()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button" });
            stackPanel.Children.Add(new TextBox { Text = "Text" });

            Application.Current.MainWindow.Content = stackPanel;
            Application.Current.MainWindow.UpdateLayout();

            var elementId = elementFinder.GenerateElementId(stackPanel);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.CompareTree(elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.CompareTree(elementId));

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
        cachedResult.GetRawText().Should().Be(lookupResult.GetRawText());
        lookupResult.GetProperty("visualChildCount").GetInt32().Should().Be(2);
        lookupResult.GetProperty("logicalChildCount").GetInt32().Should().Be(2);
        lookupResult.GetProperty("differenceCount").GetInt32().Should().Be(0);
        lookupResult.GetProperty("differences").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void CompareTree_WithTabControl_ShouldReportLogicalOnlyDifferences()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var tabControl = new TabControl();
            tabControl.Items.Add(new TabItem
            {
                Header = "First",
                Content = new TextBlock { Text = "One" }
            });
            tabControl.Items.Add(new TabItem
            {
                Header = "Second",
                Content = new TextBlock { Text = "Two" }
            });

            CreateVisibleMainWindow(tabControl);

            var elementId = elementFinder.GenerateElementId(tabControl);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.CompareTree(elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.CompareTree(elementId));

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
        lookupResult.GetProperty("differenceCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var logicalOnlyTabItems = lookupResult.GetProperty("differences").EnumerateArray()
            .Where(difference =>
                difference.GetProperty("type").GetString() == "LogicalOnly"
                && difference.GetProperty("elementType").GetString() == "TabItem")
            .ToArray();

        logicalOnlyTabItems.Should().HaveCount(2);
    }

    [Fact]
    public void CompareTree_WithVisibleWindow_ShouldReportVisualOnlyDifferences()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new TextBlock { Text = "Window content" });
            var window = CreateVisibleMainWindow(stackPanel);

            var elementId = elementFinder.GenerateElementId(window);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.CompareTree(elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.CompareTree(elementId));

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
        lookupResult.GetProperty("differenceCount").GetInt32().Should().BeGreaterThan(1);
        lookupResult.GetProperty("differences").EnumerateArray().Should().Contain(difference =>
            difference.GetProperty("type").GetString() == "LogicalOnly"
            && difference.GetProperty("elementType").GetString() == "StackPanel");
        lookupResult.GetProperty("differences").EnumerateArray().Should().Contain(difference =>
            difference.GetProperty("type").GetString() == "VisualOnly");
    }

    [Fact]
    public void GetVisualTree_FromNonUiThread_WithRootElement_ShouldStillSucceed()
    {
        using var elementFinder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(elementFinder);

        _fixture.RunOnUIThread(() =>
        {
            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button 1" });
            stackPanel.Children.Add(new TextBox { Text = "TextBox 1" });
            CreateVisibleMainWindow(stackPanel);
        });

        var result = analyzer.GetVisualTree(maxDepth: 5, elementId: null);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        var tree = json.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("Window");
        var contentStackPanel = FindNodesByType(tree, "StackPanel")
            .Single(node => node.GetProperty("childCount").GetInt32() == 2);
        GetImmediateChildTypes(contentStackPanel).Should().Equal("Button", "TextBox");
    }

    [Fact]
    public void GetVisualTree_FromNonUiThread_WithElementId_ShouldPreserveCacheParity()
    {
        using var elementFinder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(elementFinder);
        string? elementId = null;

        _fixture.RunOnUIThread(() =>
        {
            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button 1" });
            stackPanel.Children.Add(new TextBox { Text = "TextBox 1" });
            CreateVisibleMainWindow(stackPanel);
            elementId = elementFinder.GenerateElementId(stackPanel);
        });

        elementId.Should().NotBeNullOrWhiteSpace();

        var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetVisualTree(maxDepth: 3, elementId: elementId));

        _fixture.RunOnUIThread(() =>
        {
            EvictElementCacheEntry(elementFinder, elementId!);
        });

        var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetVisualTree(maxDepth: 3, elementId: elementId));

        cachedResult.GetRawText().Should().Be(lookupResult.GetRawText());
        lookupResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var tree = lookupResult.GetProperty("tree");
        tree.GetProperty("type").GetString().Should().Be("StackPanel");
        GetImmediateChildTypes(tree).Should().Equal("Button", "TextBox");
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private static JsonElement GetOnlyChildOfType(JsonElement node, string type)
    {
        var matches = node.GetProperty("children")
            .EnumerateArray()
            .Where(child => child.GetProperty("type").GetString() == type)
            .ToArray();

        matches.Should().ContainSingle();
        return matches[0];
    }

    private static string[] GetDescendantTypes(JsonElement root)
    {
        var types = new List<string>();
        CollectDescendantTypes(root, types);
        return types.ToArray();
    }

    private static JsonElement[] FindNodesByType(JsonElement root, string type)
    {
        var matches = new List<JsonElement>();
        CollectNodesByType(root, type, matches);
        return matches.ToArray();
    }

    private static void CollectNodesByType(JsonElement node, string type, List<JsonElement> matches)
    {
        if (node.GetProperty("type").GetString() == type)
        {
            matches.Add(node);
        }

        if (!node.TryGetProperty("children", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            CollectNodesByType(child, type, matches);
        }
    }

    private static string[] GetImmediateChildTypes(JsonElement node)
    {
        return node.GetProperty("children")
            .EnumerateArray()
            .Select(child => child.GetProperty("type").GetString())
            .Where(type => type != null)
            .Cast<string>()
            .ToArray();
    }

    private static void CollectDescendantTypes(JsonElement node, List<string> types)
    {
        types.Add(node.GetProperty("type").GetString()!);

        if (!node.TryGetProperty("children", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            CollectDescendantTypes(child, types);
        }
    }

    private Window CreateVisibleMainWindow(object content)
    {
        var application = Application.Current;
        application.Should().NotBeNull();

        _previousMainWindow ??= application!.MainWindow;

        var window = new Window
        {
            Width = 400,
            Height = 300,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Content = content
        };

        _activeWindow = window;
        application.MainWindow = window;
        window.Show();
        window.UpdateLayout();

        return window;
    }
}
