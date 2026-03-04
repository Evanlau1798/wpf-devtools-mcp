using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for LayoutAnalyzer requiring full WPF Application context
/// </summary>
[Collection("WpfIntegration")]
public class LayoutAnalyzerIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public LayoutAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetLayoutInfo_WithElement_ShouldReturnLayoutInfo()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);

            var button = new Button
            {
                Content = "Test Button",
                Width = 200,
                Height = 50,
                Margin = new Thickness(10)
            };

            Application.Current.MainWindow.Content = button;

            // Force layout update
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            button.Arrange(new Rect(button.DesiredSize));

            return analyzer.GetLayoutInfo(elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetClippingInfo_WithClippedElement_ShouldReturnClippingInfo()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);

            var border = new Border
            {
                Width = 100,
                Height = 100,
                ClipToBounds = true,
                Child = new TextBlock
                {
                    Text = "This is a very long text that will be clipped",
                    TextWrapping = TextWrapping.NoWrap
                }
            };

            Application.Current.MainWindow.Content = border;

            return analyzer.GetClippingInfo(elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void HighlightElement_ShouldExecuteSuccessfully()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);

            var button = new Button { Content = "Test" };
            Application.Current.MainWindow.Content = button;

            return analyzer.HighlightElement(elementId: null, color: "Red", duration: 1000);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void InvalidateLayout_ShouldExecuteSuccessfully()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);

            var button = new Button { Content = "Test" };
            Application.Current.MainWindow.Content = button;

            return analyzer.InvalidateLayout(elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }
}
