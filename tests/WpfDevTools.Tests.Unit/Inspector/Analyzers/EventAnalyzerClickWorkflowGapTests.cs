using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class EventAnalyzerClickWorkflowGapTests
{
    [StaFact]
    public void TraceRoutedEvents_WithCheckBoxClickElement_ShouldCaptureClickEvent()
    {
        var finder = new ElementFinder();
        var eventAnalyzer = new EventAnalyzer(finder);
        var interactionAnalyzer = new InteractionAnalyzer(finder);
        var checkBox = new CheckBox();
        var elementId = finder.GenerateElementId(checkBox);

        var startResult = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(eventAnalyzer.TraceRoutedEvents(elementId, "Click", 300)));
        startResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var clickResult = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(interactionAnalyzer.ClickElement(elementId)));
        clickResult.GetProperty("success").GetBoolean().Should().BeTrue(clickResult.GetRawText());

        Thread.Sleep(25);

        var trace = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(eventAnalyzer.GetEventTrace()));
        trace.GetProperty("success").GetBoolean().Should().BeTrue();
        trace.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0);
    }
}
