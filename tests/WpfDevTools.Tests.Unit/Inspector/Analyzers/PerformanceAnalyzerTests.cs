using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using System.Windows.Data;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class PerformanceAnalyzerTests
{
    public PerformanceAnalyzerTests()
    {
        // Clear tracked bindings before each test
        PerformanceAnalyzer.ClearTrackedBindings();
    }

    [StaFact]
    public void FindBindingLeaks_WithNoTrackedBindings_ShouldReturnZero()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();
        PerformanceAnalyzer.ClearTrackedBindings();

        // Act
        dynamic result = analyzer.FindBindingLeaks(100);

        // Assert
        Assert.NotNull(result);
        ((bool)result.success).Should().BeTrue();
        ((int)result.aliveBindings).Should().Be(0);
        ((int)result.totalTracked).Should().Be(0);
        ((bool)result.hasLeaks).Should().BeFalse();
    }

    [StaFact]
    public void FindBindingLeaks_WithTrackedBindings_BelowThreshold_ShouldNotDetectLeaks()
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

        // Act
        dynamic result = analyzer.FindBindingLeaks(threshold: 10);

        // Assert
        Assert.NotNull(result);
        ((bool)result.success).Should().BeTrue();
        ((int)result.aliveBindings).Should().Be(5);
        ((int)result.threshold).Should().Be(10);
        ((bool)result.hasLeaks).Should().BeFalse();
        ((string)result.message).Should().Contain("No binding leaks detected");
    }

    [StaFact]
    public void FindBindingLeaks_WithTrackedBindings_AboveThreshold_ShouldDetectLeaks()
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

        // Act
        dynamic result = analyzer.FindBindingLeaks(threshold: 10);

        // Assert
        Assert.NotNull(result);
        ((bool)result.success).Should().BeTrue();
        ((int)result.aliveBindings).Should().Be(15);
        ((int)result.threshold).Should().Be(10);
        ((bool)result.hasLeaks).Should().BeTrue();
        ((string)result.message).Should().Contain("Potential memory leak detected");
        ((string)result.recommendation).Should().Contain("event handler leaks");
    }

    [StaFact]
    public void TrackBinding_ShouldAddToTrackedList()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();
        PerformanceAnalyzer.ClearTrackedBindings();
        var binding = new Binding("TestProperty");

        // Act
        PerformanceAnalyzer.TrackBinding(binding);
        dynamic result = analyzer.FindBindingLeaks(0);

        // Assert
        ((int)result.totalTracked).Should().Be(1);
        ((int)result.aliveBindings).Should().Be(1);
    }

    [StaFact]
    public void ClearTrackedBindings_ShouldRemoveAllBindings()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();
        for (int i = 0; i < 10; i++)
        {
            PerformanceAnalyzer.TrackBinding(new Binding($"Property{i}"));
        }

        // Act
        PerformanceAnalyzer.ClearTrackedBindings();
        dynamic result = analyzer.FindBindingLeaks(0);

        // Assert
        ((int)result.totalTracked).Should().Be(0);
        ((int)result.aliveBindings).Should().Be(0);
    }

    [StaFact]
    public void FindBindingLeaks_AfterGC_ShouldRemoveDeadReferences()
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

        // Act - FindBindingLeaks forces GC
        dynamic result = analyzer.FindBindingLeaks(0);

        // Assert - Some bindings should be collected
        ((int)result.deadBindings).Should().BeGreaterThan(0);
    }

    [StaFact]
    public void GetRenderStats_ShouldReturnValidStats()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();

        // Act
        dynamic result = analyzer.GetRenderStats();

        // Assert
        Assert.NotNull(result);
        ((bool)result.success).Should().BeTrue();
        ((double)result.frameRate).Should().BeGreaterThanOrEqualTo(0);
        ((double)result.averageFrameTime).Should().BeGreaterThanOrEqualTo(0);
        ((int)result.totalFrames).Should().BeGreaterThanOrEqualTo(0);
    }

    [StaFact]
    public void GetVisualCount_WithNullElement_ShouldReturnError()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();

        // Act
        dynamic result = analyzer.GetVisualCount("nonexistent");

        // Assert
        Assert.NotNull(result);
        ((string)result.error).Should().Be("Element not found");
    }

    [StaFact]
    public void MeasureElementRenderTime_WithNullElement_ShouldReturnError()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();

        // Act
        dynamic result = analyzer.MeasureElementRenderTime("nonexistent");

        // Assert
        Assert.NotNull(result);
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Be("Element not found");
    }
}
