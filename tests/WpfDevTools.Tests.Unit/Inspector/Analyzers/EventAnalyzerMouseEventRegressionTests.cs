using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class EventAnalyzerMouseEventRegressionTests
{
    [StaFact]
    public void FireRoutedEvent_MouseDownOnTextBox_ShouldSucceed()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.FireRoutedEvent(elementId, "MouseDown", null)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("eventName").GetString().Should().Be("MouseDown");
        result.TryGetProperty("usedOnClick", out _).Should().BeFalse();
    }
}
