using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class EventHandlerInspectionContractTests
{
    [StaFact]
    public void GetEventHandlers_WithNoHandlers_ShouldExposeInspectionLimitations()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventHandlers(elementId, "Click")));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("reflectionSupported", out _).Should().BeTrue();
        result.TryGetProperty("mayBeIncomplete", out _).Should().BeTrue();
    }
}
