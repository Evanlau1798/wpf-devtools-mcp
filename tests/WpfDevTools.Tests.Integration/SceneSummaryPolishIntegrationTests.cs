using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class SceneSummaryPolishIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public SceneSummaryPolishIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetUiSummary_ShouldSkipSignalFreeEmptyTextBlocks()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new UiSummaryAnalyzer(finder);
            var root = new StackPanel
            {
                Children =
                {
                    new TextBlock { Name = "EmptySignalText", Text = string.Empty },
                    new TextBlock { Name = "ActualStatusText", Text = "Ready" }
                }
            };

            Application.Current.MainWindow.Content = root;
            var elementId = finder.GenerateElementId(root);
            return JsonSerializer.SerializeToElement(analyzer.GetUiSummary(elementId, depth: 2));
        });

        var summaryText = result.GetProperty("summaryText").GetString();
        summaryText.Should().Contain("ActualStatusText");
        summaryText.Should().NotContain("EmptySignalText");
    }

    [Fact]
    public void GetFormSummary_ShouldUseGroupBoxHeaderAsLabelFallback()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new FormSummaryAnalyzer(finder);
            var groupBox = new GroupBox
            {
                Header = "Credentials",
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBox { Name = "UsernameBox", Text = string.Empty }
                    }
                }
            };

            Application.Current.MainWindow.Content = groupBox;
            var elementId = finder.GenerateElementId(groupBox);
            return JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));
        });

        result.GetProperty("inputs")[0].GetProperty("label").GetString().Should().Be("Credentials");
    }

    [Fact]
    public void GetUiSummary_ShouldSkipFrameworkStyleDisplayTextNoise()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new UiSummaryAnalyzer(finder);
            var root = new StackPanel
            {
                Children =
                {
                    new Button { Content = new StackPanel() },
                    new TextBlock { Name = "ActualStatusText", Text = "Ready" }
                }
            };

            Application.Current.MainWindow.Content = root;
            var elementId = finder.GenerateElementId(root);
            return JsonSerializer.SerializeToElement(analyzer.GetUiSummary(elementId, depth: 2));
        });

        var summaryText = result.GetProperty("summaryText").GetString();
        summaryText.Should().Contain("ActualStatusText");
        summaryText.Should().NotContain("System.Windows");
    }
}
