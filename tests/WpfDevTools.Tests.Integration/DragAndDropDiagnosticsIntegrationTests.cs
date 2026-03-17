using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public sealed class DragAndDropDiagnosticsIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public DragAndDropDiagnosticsIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DragAndDrop_ShouldRaiseDragOverAndReportTargetHandlerHints()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new InteractionAnalyzer(finder);
            var source = new TextBox { Text = "Drag me!" };
            var target = new Border { AllowDrop = true, Width = 120, Height = 40 };
            var dragOverRaised = false;
            var dropRaised = false;

            target.AddHandler(DragDrop.DragOverEvent, new DragEventHandler((_, e) =>
            {
                dragOverRaised = true;
                e.Effects = e.Data.GetDataPresent(DataFormats.Text)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
                e.Handled = true;
            }), handledEventsToo: true);
            target.AddHandler(DragDrop.DropEvent, new DragEventHandler((_, e) =>
            {
                dropRaised = true;
                e.Handled = true;
            }), handledEventsToo: true);

            var panel = new StackPanel();
            panel.Children.Add(source);
            panel.Children.Add(target);

            var window = Application.Current.MainWindow;
            window.Content = panel;
            window.Show();
            window.Activate();
            panel.Measure(new Size(400, 200));
            panel.Arrange(new Rect(0, 0, 400, 200));
            panel.UpdateLayout();

            var sourceId = finder.GenerateElementId(source);
            var targetId = finder.GenerateElementId(target);
            var analyzerResult = analyzer.DragAndDrop(sourceId, targetId, DataFormats.Text);

            return new
            {
                AnalyzerResult = analyzerResult,
                DragOverRaised = dragOverRaised,
                DropRaised = dropRaised
            };
        });

        var doc = JsonSerializer.SerializeToElement(result.AnalyzerResult);
        var hints = doc.GetProperty("targetHandlerHints");

        doc.GetProperty("success").GetBoolean().Should().BeTrue(doc.GetRawText());
        result.DragOverRaised.Should().BeTrue();
        result.DropRaised.Should().BeTrue();
        hints.GetProperty("hasDropHandler").GetBoolean().Should().BeTrue();
        hints.GetProperty("hasDragOverHandler").GetBoolean().Should().BeTrue();
    }
}
