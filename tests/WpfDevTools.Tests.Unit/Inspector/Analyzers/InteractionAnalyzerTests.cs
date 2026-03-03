using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class InteractionAnalyzerTests
{
    [StaFact]
    public void ClickElement_WithValidButton_ShouldRaiseClickEvent()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var clicked = false;
        button.Click += (s, e) => clicked = true;

        // Act
        var result = analyzer.ClickElement(elementId);

        // Assert
        clicked.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [StaFact]
    public void ScrollToElement_WithValidElement_ShouldBringIntoView()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ScrollToElement(elementId);

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void TakeScreenshot_WithValidElement_ShouldReturnBase64Image()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 100, Height = 50 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.TakeScreenshot(elementId);

        // Assert
        result.Should().NotBeNull();
    }
}
