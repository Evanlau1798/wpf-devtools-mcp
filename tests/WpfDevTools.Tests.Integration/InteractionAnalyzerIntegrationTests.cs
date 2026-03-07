using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public class InteractionAnalyzerIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public InteractionAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DragAndDrop_WithTextDataFormat_ShouldTransferSourceText()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new InteractionAnalyzer(finder);
            var source = new TextBox { Text = "Drag me!" };
            var target = new TextBox { Text = "Drop here", AllowDrop = true };
            target.DragEnter += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.Text)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
                e.Handled = true;
            };
            target.Drop += (_, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    target.Text = e.Data.GetData(DataFormats.Text) as string ?? string.Empty;
                }

                e.Handled = true;
            };

            var panel = new StackPanel();
            panel.Children.Add(source);
            panel.Children.Add(target);
            Application.Current.MainWindow.Content = panel;
            panel.Measure(new Size(400, 200));
            panel.Arrange(new Rect(0, 0, 400, 200));
            panel.UpdateLayout();

            var sourceId = finder.GenerateElementId(source);
            var targetId = finder.GenerateElementId(target);
            var analyzerResult = analyzer.DragAndDrop(sourceId, targetId, DataFormats.Text);

            return new
            {
                AnalyzerResult = analyzerResult,
                TargetText = target.Text
            };
        });

        var doc = JsonSerializer.SerializeToElement(result.AnalyzerResult);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TargetText.Should().Be("Drag me!");
    }
}
