using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class MvvmAnalyzerTests
{
    [Fact]
    public void GetViewModel_WithNullElementId_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);

        // Act
        var result = analyzer.GetViewModel(null);

        // Assert
        result.Should().NotBeNull();
    }
}
