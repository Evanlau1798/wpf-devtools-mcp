using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("InteractionState")]
public sealed class InteractionAnalyzerWatchEventTests
{
    [StaFact]
    public void ClickElement_WhenButtonClickSucceeds_ShouldEnqueueSharedRoutedEventRecord()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());
        var analyzer = new InteractionAnalyzer(finder, buffer);
        var button = new Button { Name = "EventStormButton" };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.ClickElement(elementId)));
        result.GetProperty("success").GetBoolean().Should().BeTrue();

        var events = buffer.GetSnapshot();
        events.Should().Contain(record =>
            record.EventType == "RoutedEvent"
            && record.ElementId == elementId
            && record.EventName == "Click"
            && record.SenderType == "Button");
    }
}
