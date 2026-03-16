using System.Text.Json;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class PerformanceAnalyzerContractTests
{
    public PerformanceAnalyzerContractTests()
    {
        PerformanceAnalyzer.ClearTrackedBindings();
        PerformanceAnalyzer.ResetMonitoring();
    }

    [Fact]
    public void GetRenderStats_ShouldExposeWarmupMetadata()
    {
        var analyzer = new PerformanceAnalyzer();

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetRenderStats()));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("isWarmedUp", out _).Should().BeTrue();
        result.TryGetProperty("sampleCount", out _).Should().BeTrue();
        result.TryGetProperty("sampleWindowSize", out _).Should().BeTrue();
        result.TryGetProperty("confidence", out _).Should().BeTrue();
        result.TryGetProperty("minimumRecommendedSampleCount", out _).Should().BeTrue();
        result.TryGetProperty("minimumRecommendedMonitoringDurationMs", out _).Should().BeTrue();
        result.TryGetProperty("sampleGuidance", out _).Should().BeTrue();
    }

    [Fact]
    public void FindBindingLeaks_ShouldExposeSuspectsArray()
    {
        var analyzer = new PerformanceAnalyzer();
        PerformanceAnalyzer.TrackBinding(new Binding("Name"));

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.FindBindingLeaks(0)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("suspects", out var suspects).Should().BeTrue();
        suspects.ValueKind.Should().Be(JsonValueKind.Array);
        result.TryGetProperty("confidence", out _).Should().BeTrue();
        result.TryGetProperty("samplingDurationMs", out _).Should().BeTrue();
        result.TryGetProperty("minimumRecommendedSamplingDurationMs", out _).Should().BeTrue();
        result.TryGetProperty("sampleGuidance", out _).Should().BeTrue();
    }
}
