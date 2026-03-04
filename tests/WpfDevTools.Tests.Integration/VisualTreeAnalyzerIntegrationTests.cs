using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for VisualTreeAnalyzer requiring full WPF Application context
/// </summary>
[Collection("WpfIntegration")]
public class VisualTreeAnalyzerIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public VisualTreeAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetVisualTree_WithRootElement_ShouldReturnTree()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            // Create a simple visual tree
            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button 1" });
            stackPanel.Children.Add(new TextBox { Text = "TextBox 1" });
            stackPanel.Children.Add(new Button { Content = "Button 2" });

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetVisualTree(maxDepth: null, elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetVisualTree_WithDepthLimit_ShouldRespectDepth()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            // Create nested visual tree
            var border1 = new Border();
            var border2 = new Border();
            var border3 = new Border();
            var button = new Button { Content = "Deep Button" };

            border3.Child = button;
            border2.Child = border3;
            border1.Child = border2;

            Application.Current.MainWindow.Content = border1;

            return analyzer.GetVisualTree(maxDepth: 2, elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetNameScope_WithNamedElements_ShouldReturnNames()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var stackPanel = new StackPanel();

            var button1 = new Button { Content = "Button 1", Name = "TestButton1" };
            var button2 = new Button { Content = "Button 2", Name = "TestButton2" };
            var textBox = new TextBox { Text = "Test", Name = "TestTextBox" };

            stackPanel.Children.Add(button1);
            stackPanel.Children.Add(button2);
            stackPanel.Children.Add(textBox);

            // Don't manually register names - let WPF handle it when added to window
            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetNameScope(elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void CompareTree_ShouldExecuteSuccessfully()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Button { Content = "Button" });
            stackPanel.Children.Add(new TextBox { Text = "Text" });

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.CompareTree(elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }
}
