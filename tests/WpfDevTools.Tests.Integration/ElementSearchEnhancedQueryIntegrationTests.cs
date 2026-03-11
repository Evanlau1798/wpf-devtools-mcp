using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public sealed class ElementSearchEnhancedQueryIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public ElementSearchEnhancedQueryIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void FindElements_ShouldSupportContainsAndMultiTypeQueries()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new ElementSearchAnalyzer(finder);
            var panel = new StackPanel();
            panel.Children.Add(new Button { Name = "ErrorActionButton" });
            panel.Children.Add(new CheckBox { Name = "ErrorToggleCheckBox" });
            panel.Children.Add(new TextBox { Name = "EditorTextBox" });

            Application.Current.MainWindow.Content = panel;
            var rootId = finder.GenerateElementId(panel);

            return JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: rootId,
                elementName: "error",
                typeNames: new[] { "Button", "CheckBox" },
                matchMode: "contains"));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("resultCount").GetInt32().Should().Be(2);
    }
}
