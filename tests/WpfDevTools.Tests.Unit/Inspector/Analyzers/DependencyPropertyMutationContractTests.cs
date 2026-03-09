using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyMutationContractTests
{
    [StaFact]
    public void SetValue_ShouldReturnOldAndNewValueMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 120d };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.SetValue("Width", 240d, elementId)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyName").GetString().Should().Be("Width");
        result.GetProperty("oldValue").GetString().Should().Be("120");
        result.GetProperty("newValue").GetString().Should().Be("240");
    }

    [StaFact]
    public void ClearValue_ShouldReturnClearedAndCurrentValueMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 150d };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.ClearValue("Width", elementId)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyName").GetString().Should().Be("Width");
        result.TryGetProperty("clearedValue", out _).Should().BeTrue();
        result.TryGetProperty("newValue", out _).Should().BeTrue();
    }
}
