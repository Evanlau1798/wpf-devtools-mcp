using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DeprecatedFieldContractTests
{
    [StaFact]
    public void DependencyPropertyResponses_ShouldRetainEffectiveValueAlias()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 120 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Width", elementId));

        result.GetProperty("currentValue").GetString().Should().Be("120");
        result.GetProperty("effectiveValue").GetString().Should().Be("120");
    }

    [StaFact]
    public void ViewModelResponses_ShouldRetainViewModelTypeAlias()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var grid = new Grid { DataContext = new SampleViewModel() };
        var elementId = finder.GenerateElementId(grid);

        var result = JsonSerializer.SerializeToElement(analyzer.GetViewModel(elementId));

        result.GetProperty("typeName").GetString().Should().Be(nameof(SampleViewModel));
        result.GetProperty("viewModelType").GetString().Should().Be(nameof(SampleViewModel));
    }

    [Fact]
    public void RenderStats_ShouldRetainAverageFrameTimeAlias()
    {
        var analyzer = new PerformanceAnalyzer();

        var result = JsonSerializer.SerializeToElement(analyzer.GetRenderStats());

        result.GetProperty("avgRenderTime").GetDouble()
            .Should().Be(result.GetProperty("averageFrameTime").GetDouble());
    }

    [StaFact]
    public void VisualCount_ShouldRetainTotalCountAlias()
    {
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetVisualCount(elementId));

        result.GetProperty("count").GetInt32()
            .Should().Be(result.GetProperty("totalCount").GetInt32());
    }

    [StaFact]
    public void MeasureElementRenderTime_ShouldRetainRenderTimeAlias()
    {
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.MeasureElementRenderTime(elementId));

        result.GetProperty("renderTimeMs").GetDouble()
            .Should().Be(result.GetProperty("renderTime").GetDouble());
    }

    private sealed class SampleViewModel
    {
    }
}
