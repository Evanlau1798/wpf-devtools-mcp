using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class DependencyPropertyAnalyzerTests
{
    [StaFact]
    public void GetValueSource_WithValidProperty_ShouldReturnSource()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetValueSource("Width", elementId);

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void SetValue_WithValidProperty_ShouldSetValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.SetValue("Width", 200.0, elementId);

        // Assert
        result.Should().NotBeNull();
        button.Width.Should().Be(200.0);
    }

    [StaFact]
    public void ClearValue_WithLocalValue_ShouldClearValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ClearValue("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        button.Width.Should().Be(double.NaN); // Default value
    }

    [StaFact]
    public void GetMetadata_WithValidProperty_ShouldReturnMetadata()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetMetadata("Width", elementId);

        // Assert
        result.Should().NotBeNull();
    }
}
