using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class ElementSearchEnhancedQueryIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private Window? _activeWindow;

    public ElementSearchEnhancedQueryIntegrationTests(WpfApplicationFixture fixture)
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
    public void FindElements_WithContainsAndMultiTypeQueries_ShouldPreserveCacheParityAndReturnMatchingResults()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var finder = new ElementFinder();
            var analyzer = new ElementSearchAnalyzer(finder);
            var searchPanel = new StackPanel();
            searchPanel.Children.Add(new Button { Name = "ErrorActionButton" });
            searchPanel.Children.Add(new CheckBox { Name = "ErrorToggleCheckBox" });
            searchPanel.Children.Add(new TextBox { Name = "EditorTextBox" });

            var root = new Border
            {
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Header" },
                        searchPanel
                    }
                }
            };

            CreateVisibleMainWindow(root);

            var rootId = finder.GenerateElementId(searchPanel);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: rootId,
                elementName: "error",
                typeNames: new[] { "Button", "CheckBox" },
                matchMode: "contains"));
            EvictElementCacheEntry(finder, rootId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: rootId,
                elementName: "error",
                typeNames: new[] { "Button", "CheckBox" },
                matchMode: "contains"));

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
        lookupResult.GetProperty("resultCount").GetInt32().Should().Be(2);
        lookupResult.GetProperty("truncated").GetBoolean().Should().BeFalse();

        var matches = lookupResult.GetProperty("results").EnumerateArray().ToArray();
        matches.Should().ContainSingle(match =>
            match.GetProperty("elementType").GetString() == "Button"
            && match.GetProperty("elementName").GetString() == "ErrorActionButton"
            && !string.IsNullOrWhiteSpace(match.GetProperty("elementId").GetString()));
        matches.Should().ContainSingle(match =>
            match.GetProperty("elementType").GetString() == "CheckBox"
            && match.GetProperty("elementName").GetString() == "ErrorToggleCheckBox"
            && !string.IsNullOrWhiteSpace(match.GetProperty("elementId").GetString()));
    }

    [Fact]
    public void FindElements_FromNonUiThread_WithDefaultRoot_ShouldUseExactMatching()
    {
        using var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);

        _fixture.RunOnUIThread(() =>
        {
            var panel = new StackPanel();
            panel.Children.Add(new Button { Name = "ErrorActionButton" });
            panel.Children.Add(new Button { Name = "ErrorActionButtonSecondary" });
            panel.Children.Add(new CheckBox { Name = "ErrorActionCheckBox" });

            CreateVisibleMainWindow(panel);
        });

        var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
            typeName: "Button",
            elementName: "ErrorActionButton"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("resultCount").GetInt32().Should().Be(1);
        result.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var match = result.GetProperty("results")[0];
        match.GetProperty("elementType").GetString().Should().Be("Button");
        match.GetProperty("elementName").GetString().Should().Be("ErrorActionButton");
    }

    [Fact]
    public void FindElements_WithPropertyFilter_ShouldReturnMatchedMetadataAndCacheParity()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var finder = new ElementFinder();
            var analyzer = new ElementSearchAnalyzer(finder);
            var searchPanel = new StackPanel();
            searchPanel.Children.Add(new TextBlock { Name = "StatusReadyText", Text = "Ready" });
            searchPanel.Children.Add(new TextBlock { Name = "StatusBusyText", Text = "Busy" });

            CreateVisibleMainWindow(searchPanel);

            var rootId = finder.GenerateElementId(searchPanel);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: rootId,
                typeName: "TextBlock",
                propertyName: "Text",
                propertyValue: "Ready"));
            EvictElementCacheEntry(finder, rootId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: rootId,
                typeName: "TextBlock",
                propertyName: "Text",
                propertyValue: "Ready"));

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
        lookupResult.GetProperty("resultCount").GetInt32().Should().Be(1);
        var match = lookupResult.GetProperty("results")[0];
        match.GetProperty("elementType").GetString().Should().Be("TextBlock");
        match.GetProperty("elementName").GetString().Should().Be("StatusReadyText");
        match.GetProperty("matchedProperty").GetString().Should().Be("Text");
        match.GetProperty("matchedValue").GetString().Should().Be("Ready");
    }

    [Fact]
    public void FindElements_WithNoMatches_ShouldReturnEmptyResults()
    {
        using var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);

        _fixture.RunOnUIThread(() =>
        {
            var panel = new StackPanel();
            panel.Children.Add(new Button { Name = "SaveButton" });
            panel.Children.Add(new TextBox { Name = "EditorTextBox" });

            CreateVisibleMainWindow(panel);
        });

        var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
            typeName: "Button",
            elementName: "MissingButton"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("resultCount").GetInt32().Should().Be(0);
        result.GetProperty("truncated").GetBoolean().Should().BeFalse();
        result.GetProperty("results").GetArrayLength().Should().Be(0);
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

    private static void EvictElementCacheEntry(ElementFinder finder, string elementId)
    {
        finder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }
}
