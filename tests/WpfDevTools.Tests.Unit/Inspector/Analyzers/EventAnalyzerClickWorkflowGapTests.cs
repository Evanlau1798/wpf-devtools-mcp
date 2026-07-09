using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("TimingSensitive")]
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
            JsonSerializer.Serialize(eventAnalyzer.TraceRoutedEvents(elementId, "Click", 5000)));
        startResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var clickResult = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(interactionAnalyzer.ClickElement(elementId)));
        clickResult.GetProperty("success").GetBoolean().Should().BeTrue(clickResult.GetRawText());

        var trace = WaitForCapturedEvent(eventAnalyzer);
        trace.GetProperty("success").GetBoolean().Should().BeTrue();
        trace.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0);
    }

    private static JsonElement WaitForCapturedEvent(EventAnalyzer eventAnalyzer)
    {
        JsonElement trace = default;
        var captured = SpinWait.SpinUntil(() =>
        {
            trace = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(eventAnalyzer.GetEventTrace()));
            return trace.GetProperty("eventCount").GetInt32() > 0;
        }, TimeSpan.FromSeconds(2));

        captured.Should().BeTrue(trace.GetRawText());
        return trace;
    }
}
