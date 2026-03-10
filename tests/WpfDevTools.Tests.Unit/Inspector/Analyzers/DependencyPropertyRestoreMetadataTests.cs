using System.Text.Json;
using System.Windows.Controls;
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
}
