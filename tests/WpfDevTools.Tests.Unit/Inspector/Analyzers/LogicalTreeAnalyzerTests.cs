using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class LogicalTreeAnalyzerTests
{
    [Fact]
    public void GetLogicalTree_WithNullElement_ShouldReturnRoot()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LogicalTreeAnalyzer(finder);

        // Act
        var result = analyzer.GetLogicalTree(null, null);

        // Assert
        result.Should().NotBeNull();
    }
}
