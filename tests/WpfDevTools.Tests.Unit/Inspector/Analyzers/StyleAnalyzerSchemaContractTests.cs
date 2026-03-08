using System.Text.Json;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class StyleAnalyzerSchemaContractTests
{
    [StaFact]
    public void GetAppliedStyles_ShouldExposeDocumentedStyleTypeAndSetterDetails()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.WidthProperty, 100.0));
        button.Style = style;
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetAppliedStyles(elementId);
        var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("styles")[0].GetProperty("styleType").GetString().Should().Be("Explicit");
        doc.GetProperty("styles")[0].GetProperty("setters")[0].GetProperty("property").GetString().Should().Be("Width");
    }

    [StaFact]
    public void GetTriggers_ShouldExposeDocumentedTriggerMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Triggers.Add(new Trigger
        {
            Property = Button.IsEnabledProperty,
            Value = false,
            Setters = { new Setter(Button.OpacityProperty, 0.5) }
        });
        button.Style = style;
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetTriggers(elementId);
        var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("triggerCount").GetInt32().Should().Be(1);
        doc.GetProperty("triggers")[0].GetProperty("triggerType").GetString().Should().Be("Property");
        doc.GetProperty("triggers")[0].GetProperty("conditions")[0].GetProperty("property").GetString().Should().Be("IsEnabled");
        doc.GetProperty("triggers")[0].GetProperty("setters")[0].GetProperty("property").GetString().Should().Be("Opacity");
    }
}
