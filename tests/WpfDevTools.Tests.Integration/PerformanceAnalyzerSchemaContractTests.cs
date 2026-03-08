using System.Text.Json;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public class PerformanceAnalyzerSchemaContractTests
{
    private readonly WpfApplicationFixture _fixture;

    public PerformanceAnalyzerSchemaContractTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetRenderStats_ShouldExposeDocumentedAliases()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var panel = new StackPanel();
            Application.Current.MainWindow.Content = panel;
            var analyzer = new PerformanceAnalyzer();
            return analyzer.GetRenderStats();
        });

        var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("avgRenderTime", out _).Should().BeTrue();
        doc.TryGetProperty("dirtyRegionCount", out _).Should().BeTrue();
    }

    [Fact]
    public void GetVisualCount_ShouldExposeDocumentedCountAlias()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            Application.Current.MainWindow.Content = new StackPanel();
            var analyzer = new PerformanceAnalyzer();
            return analyzer.GetVisualCount(null);
        });

        var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("count", out _).Should().BeTrue();
    }

    [Fact]
    public void MeasureElementRenderTime_ShouldExposeDocumentedRenderTimeMs()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            Application.Current.MainWindow.Content = new StackPanel();
            var analyzer = new PerformanceAnalyzer();
            return analyzer.MeasureElementRenderTime(null);
        });

        var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("renderTimeMs", out _).Should().BeTrue();
    }
}
