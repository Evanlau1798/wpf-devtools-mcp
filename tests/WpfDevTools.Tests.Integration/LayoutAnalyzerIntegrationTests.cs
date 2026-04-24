using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

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
            var buttonId = elementFinder.GenerateElementId(button);

            Application.Current.MainWindow.Content = button;

            // Force layout update
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            button.Arrange(new Rect(button.DesiredSize));
            Application.Current.MainWindow.UpdateLayout();

            return analyzer.GetLayoutInfo(buttonId);
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("width").GetDouble().Should().Be(200);
        json.GetProperty("height").GetDouble().Should().Be(50);
        json.GetProperty("margin").GetProperty("left").GetDouble().Should().Be(10);
        json.GetProperty("desiredWidth").GetDouble().Should().BeGreaterThan(0);
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
                Clip = new RectangleGeometry(new Rect(0, 0, 80, 60))
            };
            var borderId = elementFinder.GenerateElementId(border);

            Application.Current.MainWindow.Content = border;
            Application.Current.MainWindow.UpdateLayout();

            return analyzer.GetClippingInfo(borderId);
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("isClipped").GetBoolean().Should().BeTrue();
        json.GetProperty("hasClip").GetBoolean().Should().BeTrue();
        json.GetProperty("clipBounds").GetProperty("width").GetDouble().Should().Be(80);
        json.GetProperty("clipBounds").GetProperty("height").GetDouble().Should().Be(60);
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
            var buttonId = elementFinder.GenerateElementId(button);
            Application.Current.MainWindow.Content = new AdornerDecorator { Child = button };
            Application.Current.MainWindow.UpdateLayout();

            return analyzer.HighlightElement(buttonId, color: "Red", duration: 1000);
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("color").GetString().Should().Be("Red");
        json.GetProperty("duration").GetInt32().Should().Be(1000);
        json.GetProperty("elementType").GetString().Should().Be("Button");
    }

    [Fact]
    public void InvalidateLayout_ShouldExecuteSuccessfully()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);

            var targetButton = new Button { Content = "Target" };
            var targetButtonId = elementFinder.GenerateElementId(targetButton);
            var secondaryWindow = new Window
            {
                Width = 300,
                Height = 200,
                Content = targetButton
            };

            try
            {
                secondaryWindow.Show();
                secondaryWindow.UpdateLayout();

                targetButton.InvalidateMeasure();
                targetButton.InvalidateArrange();

                var wasTargetMeasureInvalid = !targetButton.IsMeasureValid;
                var wasTargetArrangeInvalid = !targetButton.IsArrangeValid;

                var invalidateResult = analyzer.InvalidateLayout(targetButtonId);
                return new
                {
                    result = invalidateResult,
                    wasTargetMeasureInvalid,
                    wasTargetArrangeInvalid,
                    isTargetMeasureValid = targetButton.IsMeasureValid,
                    isTargetArrangeValid = targetButton.IsArrangeValid
                };
            }
            finally
            {
                secondaryWindow.Close();
            }
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("wasTargetMeasureInvalid").GetBoolean().Should().BeTrue();
        json.GetProperty("wasTargetArrangeInvalid").GetBoolean().Should().BeTrue();
        json.GetProperty("isTargetMeasureValid").GetBoolean().Should().BeTrue();
        json.GetProperty("isTargetArrangeValid").GetBoolean().Should().BeTrue();
        json.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
