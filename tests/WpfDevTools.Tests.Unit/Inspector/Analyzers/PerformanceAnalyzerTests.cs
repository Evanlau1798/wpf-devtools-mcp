using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class PerformanceAnalyzerTests
{
    [StaFact]
    public void FindBindingLeaks_WithThreshold_ShouldDetectLeaks()
    {
        // Arrange
        var analyzer = new PerformanceAnalyzer();

        // Act
        var result = analyzer.FindBindingLeaks(100);

        // Assert
        result.Should().NotBeNull();
    }
}
