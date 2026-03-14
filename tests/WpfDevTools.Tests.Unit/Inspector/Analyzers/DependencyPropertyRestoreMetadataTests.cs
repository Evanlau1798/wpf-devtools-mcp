using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyRestoreMetadataTests
{
    [StaFact]
    public void GetValueSource_ShouldExposeLocalValueRestoreMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 120 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Width", elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("currentValue").GetString().Should().Be("120");
        result.GetProperty("hadLocalValue").GetBoolean().Should().BeTrue();
        result.GetProperty("localValue").GetString().Should().Be("120");
    }

    [StaFact]
    public void SetValue_ShouldExposePreviousLocalValueMetadata_ForRestore()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 120 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.SetValue("Width", 180d, elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hadLocalValueBefore").GetBoolean().Should().BeTrue();
        result.GetProperty("previousLocalValue").GetString().Should().Be("120");
        result.GetProperty("previousBaseValueSource").GetString().Should().Be("Local");
    }

    [StaFact]
    public void SetValue_OnExpressionBackedProperty_ShouldReportExpressionReplacementRisk()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var textBox = new TextBox();
        BindingOperations.SetBinding(textBox, TextBox.TextProperty, new Binding(nameof(SampleViewModel.Name))
        {
            Source = new SampleViewModel { Name = "Alice" }
        });
        var elementId = finder.GenerateElementId(textBox);

        var result = JsonSerializer.SerializeToElement(analyzer.SetValue("Text", "Bob", elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("replacedExpression").GetBoolean().Should().BeTrue();
    }

    private sealed class SampleViewModel
    {
        public string Name { get; init; } = string.Empty;
    }
}
