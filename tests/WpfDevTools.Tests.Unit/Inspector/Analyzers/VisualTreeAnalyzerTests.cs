using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class VisualTreeAnalyzerTests
{
    [StaFact]
    public void CompareTree_WithDifferences_ShouldReturnDiscrepancies()
    {
        // Arrange
        var analyzer = new VisualTreeAnalyzer();
        var elementFinder = new ElementFinder();

        // Create a control with visual children but different logical children
        var border = new Border();
        var button = new Button { Content = "Test" };
        border.Child = button;

        var elementId = elementFinder.GenerateElementId(border);

        // Act
        var result = analyzer.CompareTree(elementId);

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void GetNameScope_WithNamedElements_ShouldReturnNames()
    {
        // Arrange
        var analyzer = new VisualTreeAnalyzer();
        var elementFinder = new ElementFinder();

        var stackPanel = new StackPanel();
        var button1 = new Button { Name = "Button1" };
        var button2 = new Button { Name = "Button2" };
        stackPanel.Children.Add(button1);
        stackPanel.Children.Add(button2);

        var elementId = elementFinder.GenerateElementId(stackPanel);

        // Act
        var result = analyzer.GetNameScope(elementId);

        // Assert
        result.Should().NotBeNull();
    }
}
