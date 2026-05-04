using System.Text.Json;
using System.Windows.Data;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("AnalyzerStaticState")]
public sealed class PerformanceAnalyzerContractTests
{
    private const int LegacyVisualCountBudget = 1000;

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
        result.TryGetProperty("warmUpApplied", out _).Should().BeTrue();
        result.TryGetProperty("minimumRecommendedSampleCount", out _).Should().BeTrue();
        result.TryGetProperty("minimumRecommendedMonitoringDurationMs", out _).Should().BeTrue();
        result.TryGetProperty("sampleGuidance", out _).Should().BeTrue();
        result.TryGetProperty("visualCountLimit", out _).Should().BeTrue();
        result.TryGetProperty("visualCountTruncated", out _).Should().BeTrue();
    }

    [Fact]
    public async Task FindBindingLeaksAsync_ShouldExposeSuspectsArray()
    {
        var analyzer = new PerformanceAnalyzer();
        PerformanceAnalyzer.TrackBinding(new Binding("Name"));

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(await analyzer.FindBindingLeaksAsync(0)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("suspects", out var suspects).Should().BeTrue();
        suspects.ValueKind.Should().Be(JsonValueKind.Array);
        result.TryGetProperty("confidence", out _).Should().BeTrue();
        result.TryGetProperty("warmUpApplied", out _).Should().BeTrue();
        result.TryGetProperty("samplingDurationMs", out _).Should().BeTrue();
        result.TryGetProperty("minimumRecommendedSamplingDurationMs", out _).Should().BeTrue();
        result.TryGetProperty("sampleGuidance", out _).Should().BeTrue();
    }

    [Fact]
    public async Task FindBindingLeaksAsync_WithSamplingDuration_ShouldObserveCancellation()
    {
        var analyzer = new PerformanceAnalyzer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => analyzer.FindBindingLeaksAsync(
            samplingDurationMs: 15000,
            cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FindBindingLeaksAsync_WithWarmUpSamplingWindow_ShouldObserveCancellation()
    {
        var analyzer = new PerformanceAnalyzer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => analyzer.FindBindingLeaksAsync(
            warmUp: true,
            cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [StaFact]
    public void GetVisualCount_WhenTreeExceedsLegacyBudget_ShouldReturnExactCount()
    {
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);
        var root = new StackPanel();
        for (var index = 0; index <= LegacyVisualCountBudget; index++)
        {
            root.Children.Add(new Button());
        }

        var elementId = finder.GenerateElementId(root);
        var expectedCount = root.Children.Count + 1;

        var result = JsonSerializer.SerializeToElement(analyzer.GetVisualCount(elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("count").GetInt32().Should().Be(expectedCount);
        result.GetProperty("visualCountLimit").GetInt32()
            .Should().Be(expectedCount);
        result.GetProperty("visualCountTruncated").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public void GetVisualCount_WhenTreeMatchesLegacyBudgetBoundary_ShouldStillReturnExactCount()
    {
        var finder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(finder);
        var root = new StackPanel();
        for (var index = 1; index < LegacyVisualCountBudget; index++)
        {
            root.Children.Add(new Button());
        }

        var elementId = finder.GenerateElementId(root);
        var expectedCount = root.Children.Count + 1;

        var result = JsonSerializer.SerializeToElement(analyzer.GetVisualCount(elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("count").GetInt32().Should().Be(expectedCount);
        result.GetProperty("visualCountLimit").GetInt32().Should().Be(expectedCount);
        result.GetProperty("visualCountTruncated").GetBoolean().Should().BeFalse();
    }
}
