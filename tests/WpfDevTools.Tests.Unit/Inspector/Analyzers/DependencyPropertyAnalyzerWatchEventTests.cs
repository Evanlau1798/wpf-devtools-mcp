using FluentAssertions;
using System.Windows.Controls;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyAnalyzerWatchEventTests : IDisposable
{
    public DependencyPropertyAnalyzerWatchEventTests()
    {
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
    }

    [StaFact]
    public void WatchChanges_WhenPropertyChanges_ShouldEnqueueDpChangeIntoSharedWatchBuffer()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());
        var analyzer = new DependencyPropertyAnalyzer(finder, buffer);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        analyzer.WatchChanges("Width", elementId);
        button.Width = 120;
        Thread.Sleep(50);

        var events = buffer.GetSnapshot();
        events.Should().ContainSingle();
        events[0].EventType.Should().Be("DpChange");
        events[0].ElementId.Should().Be(elementId);
        events[0].PropertyName.Should().Be("Width");
        events[0].NewValue.Should().Be("120");
    }

    [StaFact]
    public void WatchChanges_WhenSamePropertyChangesRepeatedly_ShouldDeduplicateSharedWatchBufferEntry()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());
        var analyzer = new DependencyPropertyAnalyzer(finder, buffer);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        analyzer.WatchChanges("Width", elementId);
        button.Width = 100;
        button.Width = 200;
        Thread.Sleep(50);

        var events = buffer.GetSnapshot();
        events.Should().ContainSingle();
        events[0].NewValue.Should().Be("200");
    }

    public void Dispose()
    {
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
    }
}
