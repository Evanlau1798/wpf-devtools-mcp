using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class EventAnalyzerWatchEventTests
{
    [StaFact]
    public void TraceRoutedEvents_WhenEventIsCaptured_ShouldEnqueueSharedWatchEventRecord()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());
        var analyzer = new EventAnalyzer(finder, buffer);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var start = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 5000)));
        start.GetProperty("success").GetBoolean().Should().BeTrue();

        var fire = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.FireRoutedEvent(elementId, "Click", null)));
        fire.GetProperty("success").GetBoolean().Should().BeTrue();

        var events = buffer.GetSnapshot();
        events.Should().NotBeEmpty();
        events.Should().Contain(record => record.EventType == "RoutedEvent"
            && record.ElementId == elementId
            && record.EventName == "Click");
    }

    [StaFact]
    public void FireRoutedEvent_WhenEventIsRaised_ShouldEnqueueSharedWatchEventRecord()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());
        var analyzer = new EventAnalyzer(finder, buffer);
        var button = new Button { Name = "FireWatchButton" };
        var elementId = finder.GenerateElementId(button);

        var fire = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.FireRoutedEvent(elementId, "Click", null)));
        fire.GetProperty("success").GetBoolean().Should().BeTrue();

        var events = buffer.GetSnapshot();
        events.Should().Contain(record => record.EventType == "RoutedEvent"
            && record.ElementId == elementId
            && record.EventName == "Click"
            && record.SenderType == "Button");
    }
}
