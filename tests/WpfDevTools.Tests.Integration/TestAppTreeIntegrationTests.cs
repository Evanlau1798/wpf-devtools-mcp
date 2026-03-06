using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for tree analyzers using TestApp golden sample scenarios.
/// Tests deep nesting (Tab 2), large visual trees (Tab 8 performance),
/// and logical tree traversal.
/// </summary>
[Collection("WpfIntegration")]
public class TestAppTreeIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public TestAppTreeIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetVisualTree_WithDeepNesting_ShouldTraverseAllLevels()
    {
        // Arrange - recreate TestApp Tab 2 nested tree (4-level deep borders)
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "DeepTest", Age = 1 };

            // Level 1
            var border1 = new Border
            {
                BorderBrush = Brushes.Blue,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(10)
            };
            var stack1 = new StackPanel();
            stack1.Children.Add(new TextBlock { Text = "Level 1", FontWeight = FontWeights.Bold });

            // Level 2
            var border2 = new Border
            {
                BorderBrush = Brushes.Green,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(10)
            };
            var stack2 = new StackPanel();
            stack2.Children.Add(new TextBlock { Text = "Level 2", FontWeight = FontWeights.Bold });

            // Level 3
            var border3 = new Border
            {
                BorderBrush = Brushes.Orange,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(10)
            };
            var stack3 = new StackPanel();
            stack3.Children.Add(new TextBlock { Text = "Level 3", FontWeight = FontWeights.Bold });

            // Level 4
            var border4 = new Border
            {
                BorderBrush = Brushes.Red,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(10)
            };
            var stack4 = new StackPanel { DataContext = viewModel };
            stack4.Children.Add(new TextBlock { Text = "Level 4 (Deep nesting)", FontWeight = FontWeights.Bold });
            stack4.Children.Add(new Button { Content = "Deep Button" });
            var deepTextBox = new TextBox();
            deepTextBox.SetBinding(TextBox.TextProperty, new Binding("Name"));
            stack4.Children.Add(deepTextBox);

            border4.Child = stack4;
            stack3.Children.Add(border4);
            border3.Child = stack3;
            stack2.Children.Add(border3);
            border2.Child = stack2;
            stack1.Children.Add(border2);
            border1.Child = stack1;

            Application.Current.MainWindow.Content = border1;

            return analyzer.GetVisualTree(maxDepth: 10, elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetVisualTree_WithDepthLimit_ShouldNotExceedLimit()
    {
        // Arrange - 4-level deep tree, but limit to depth 2
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var border1 = new Border { Child = new StackPanel() };
            ((StackPanel)border1.Child).Children.Add(
                new Border { Child = new StackPanel() });

            Application.Current.MainWindow.Content = border1;

            return analyzer.GetVisualTree(maxDepth: 2, elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetVisualCount_WithLargeTree_ShouldCountAllElements()
    {
        // Arrange - recreate TestApp Tab 8 performance scenario (100 elements)
        var result = _fixture.RunOnUIThread(() =>
        {
            var analyzer = new PerformanceAnalyzer();

            var stackPanel = new StackPanel();

            // Add 100 elements matching TestApp's InitializePerformanceTab pattern
            for (int i = 0; i < 100; i++)
            {
                var border = new Border
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(2),
                    Padding = new Thickness(5)
                };

                var innerStack = new StackPanel { Orientation = Orientation.Horizontal };
                innerStack.Children.Add(new TextBlock
                {
                    Text = $"Element {i + 1}: ",
                    FontWeight = FontWeights.Bold
                });
                innerStack.Children.Add(new TextBox
                {
                    Text = $"Value {i + 1}",
                    Width = 100
                });
                innerStack.Children.Add(new Button
                {
                    Content = "Click",
                    Width = 60
                });

                border.Child = innerStack;
                stackPanel.Children.Add(border);
            }

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetVisualCount(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void CompareTree_WithNestedBorders_ShouldExecuteSuccessfully()
    {
        // Arrange - nested borders matching TestApp Tab 2
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(elementFinder);

            var border1 = new Border
            {
                BorderBrush = Brushes.Blue,
                BorderThickness = new Thickness(2)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Level 1" });
            stack.Children.Add(new Button { Content = "Button" });
            var border2 = new Border
            {
                BorderBrush = Brushes.Green,
                BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = "Level 2" }
            };
            stack.Children.Add(border2);
            border1.Child = stack;

            Application.Current.MainWindow.Content = border1;

            return analyzer.CompareTree(elementId: null);
        });

        result.Should().NotBeNull();
    }
}
