using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for PerformanceAnalyzer requiring full WPF Application context
/// These tests verify methods execute without exceptions in real WPF environment
/// </summary>
public class PerformanceAnalyzerIntegrationTests : IClassFixture<WpfApplicationFixture>
{
    private readonly WpfApplicationFixture _fixture;

    public PerformanceAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        PerformanceAnalyzer.ClearTrackedBindings();
    }

    [Fact]
    public void FindBindingLeaks_WithNoTrackedBindings_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();
        PerformanceAnalyzer.ClearTrackedBindings();

        // Act & Assert - should not throw
        var result = _fixture.RunOnUIThread(() => analyzer.FindBindingLeaks(100));
        result.Should().NotBeNull();
    }

    [Fact]
    public void FindBindingLeaks_WithTrackedBindings_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();
        PerformanceAnalyzer.ClearTrackedBindings();

        // Track 5 bindings
        for (int i = 0; i < 5; i++)
        {
            var binding = new Binding($"Property{i}");
            PerformanceAnalyzer.TrackBinding(binding);
        }

        // Act & Assert - should not throw
        var result = _fixture.RunOnUIThread(() => analyzer.FindBindingLeaks(threshold: 10));
        result.Should().NotBeNull();
    }

    [Fact]
    public void FindBindingLeaks_WithManyTrackedBindings_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();
        PerformanceAnalyzer.ClearTrackedBindings();

        // Track 15 bindings
        for (int i = 0; i < 15; i++)
        {
            var binding = new Binding($"Property{i}");
            PerformanceAnalyzer.TrackBinding(binding);
        }

        // Act & Assert - should not throw
        var result = _fixture.RunOnUIThread(() => analyzer.FindBindingLeaks(threshold: 10));
        result.Should().NotBeNull();
    }

    [Fact]
    public void FindBindingLeaks_AfterGC_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();
        PerformanceAnalyzer.ClearTrackedBindings();

        // Track bindings in a scope that will be collected
        void TrackTemporaryBindings()
        {
            for (int i = 0; i < 10; i++)
            {
                var binding = new Binding($"Temp{i}");
                PerformanceAnalyzer.TrackBinding(binding);
            }
        }

        TrackTemporaryBindings();

        // Act & Assert - should not throw, GC should collect some bindings
        var result = _fixture.RunOnUIThread(() => analyzer.FindBindingLeaks(0));
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetRenderStats_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();

        // Act & Assert - should not throw
        var result = _fixture.RunOnUIThread(() => analyzer.GetRenderStats());
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetVisualCount_WithRootElement_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();

        // Act & Assert - should not throw
        var result = _fixture.RunOnUIThread(() => analyzer.GetVisualCount(null));
        result.Should().NotBeNull();
    }

    [Fact]
    public void MeasureElementRenderTime_WithRootElement_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();

        // Act & Assert - should not throw
        var result = _fixture.RunOnUIThread(() => analyzer.MeasureElementRenderTime(null));
        result.Should().NotBeNull();
    }
}
