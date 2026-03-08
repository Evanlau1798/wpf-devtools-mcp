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
[Collection("WpfIntegration")]
public class LogicalTreeAnalyzerIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public LogicalTreeAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetLogicalTree_FromNonUiThread_WithRootElement_ShouldStillSucceed()
    {
        var analyzer = new LogicalTreeAnalyzer(new ElementFinder());

        _fixture.RunOnUIThread(() =>
        {
            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button 1" });
            stackPanel.Children.Add(new TextBox { Text = "TextBox 1" });
            Application.Current.MainWindow.Content = stackPanel;
        });

        var result = analyzer.GetLogicalTree(depth: 3, elementId: null);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("tree").ValueKind.Should().Be(JsonValueKind.Object);
    }
}
