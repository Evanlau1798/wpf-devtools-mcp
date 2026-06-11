using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class StyleAnalyzerRestoreMetadataTests
{
    [StaFact]
    public void OverrideStyleSetter_ShouldExposePreviousLocalValueMetadata_ForRestore()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.WidthProperty, 110d));
        button.Style = style;
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.OverrideStyleSetter(elementId, "Width", 210d));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hadLocalValueBefore").GetBoolean().Should().BeFalse();
        result.GetProperty("previousLocalValue").ValueKind.Should().Be(JsonValueKind.Null);
        result.GetProperty("previousBaseValueSource").GetString().Should().Be("Style");
    }
}
