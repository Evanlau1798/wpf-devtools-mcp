using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class StyleAnalyzerMutationContractTests
{
    [StaFact]
    public void OverrideStyleSetter_ShouldReturnOldAndNewValueMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.WidthProperty, 110d));
        button.Style = style;
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.OverrideStyleSetter(elementId, "Width", 210d)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyName").GetString().Should().Be("Width");
        result.GetProperty("oldValue").GetString().Should().Be("110");
        result.GetProperty("newValue").GetString().Should().Be("210");
    }
}
