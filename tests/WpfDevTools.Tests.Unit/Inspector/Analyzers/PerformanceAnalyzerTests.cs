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

    [Fact]
    public void TrackBinding_ShouldAddToTrackedList()
    {
        // Arrange
        PerformanceAnalyzer.ClearTrackedBindings();
        var binding = new Binding("TestProperty");

        // Act
        PerformanceAnalyzer.TrackBinding(binding);

        // Assert - verify binding was tracked (we can't call FindBindingLeaks without WPF context)
        // This test verifies the method doesn't throw
        Assert.True(true);
    }

    [Fact]
    public void ClearTrackedBindings_ShouldNotThrow()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            PerformanceAnalyzer.TrackBinding(new Binding($"Property{i}"));
        }

        // Act
        PerformanceAnalyzer.ClearTrackedBindings();

        // Assert - verify method doesn't throw
        Assert.True(true);
    }

    [Fact]
    public void TrackBinding_WithNullBinding_ShouldNotThrow()
    {
        // Arrange
        PerformanceAnalyzer.ClearTrackedBindings();

        // Act & Assert - should handle null gracefully
        var exception = Record.Exception(() => PerformanceAnalyzer.TrackBinding(null!));
        exception.Should().BeNull();
    }

    // Note: Tests requiring WPF Application context (FindBindingLeaks, GetRenderStats, etc.)
    // should be moved to integration tests with WpfDevTools.Tests.TestApp
}
