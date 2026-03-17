using System.Text.Json;
using System.Windows.Controls;
using System.Windows;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class InteractionAnalyzerDragDropDiagnosticsTests
{
    [StaFact]
    public void DragAndDrop_WithDropAndDragOverHandlers_ShouldReportHandlerHintsAndRaiseDragOver()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var source = new TextBox { Text = "Drag me!" };
        var target = new Border { AllowDrop = true };
        var dragOverRaised = false;
        var dropRaised = false;

        target.AddHandler(DragDrop.DragOverEvent, new DragEventHandler((_, e) =>
        {
            dragOverRaised = true;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }), handledEventsToo: true);
        target.AddHandler(DragDrop.DropEvent, new DragEventHandler((_, e) =>
        {
            dropRaised = true;
            e.Handled = true;
        }), handledEventsToo: true);

        var sourceId = finder.GenerateElementId(source);
        var targetId = finder.GenerateElementId(target);

        var result = JsonSerializer.SerializeToElement(analyzer.DragAndDrop(sourceId, targetId, DataFormats.Text));
        var hints = result.GetProperty("targetHandlerHints");

        result.GetProperty("success").GetBoolean().Should().BeTrue(result.GetRawText());
        dragOverRaised.Should().BeTrue("drag_and_drop should raise DragOver before Drop");
        dropRaised.Should().BeTrue();
        hints.GetProperty("targetAllowsDrop").GetBoolean().Should().BeTrue();
        hints.GetProperty("hasDropHandler").GetBoolean().Should().BeTrue();
        hints.GetProperty("hasDragOverHandler").GetBoolean().Should().BeTrue();
        hints.GetProperty("hasAnyDropOrDragOverHandler").GetBoolean().Should().BeTrue();
        hints.GetProperty("mayBeIncomplete").GetBoolean().Should().BeTrue();
    }

    [StaFact]
    public void DragAndDrop_WithoutDropHandlers_ShouldReportMissingHandlerHints()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var source = new TextBox { Text = "Drag me!" };
        var target = new Border { AllowDrop = true };

        var sourceId = finder.GenerateElementId(source);
        var targetId = finder.GenerateElementId(target);

        var result = JsonSerializer.SerializeToElement(analyzer.DragAndDrop(sourceId, targetId, DataFormats.Text));
        var hints = result.GetProperty("targetHandlerHints");

        result.GetProperty("success").GetBoolean().Should().BeTrue(result.GetRawText());
        hints.GetProperty("targetAllowsDrop").GetBoolean().Should().BeTrue();
        hints.GetProperty("hasDropHandler").GetBoolean().Should().BeFalse();
        hints.GetProperty("hasDragOverHandler").GetBoolean().Should().BeFalse();
        hints.GetProperty("hasAnyDropOrDragOverHandler").GetBoolean().Should().BeFalse();
    }
}
