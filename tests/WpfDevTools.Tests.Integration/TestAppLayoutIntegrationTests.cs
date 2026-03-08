using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for LayoutAnalyzer using TestApp golden sample scenarios.
/// Tests clipping, transforms, and layout info matching TestApp Tab 6
/// (Layout &amp; Transforms).
/// </summary>
[Collection("WpfIntegration")]
public class TestAppLayoutIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public TestAppLayoutIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetClippingInfo_WithClipToBounds_ShouldDetectClipping()
    {
        // Arrange - recreate TestApp Tab 6 clipping scenario
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);
            var clippedText = new TextBlock
            {
                Text = "This text is very long and will be clipped by the border because ClipToBounds is True",
                TextWrapping = TextWrapping.NoWrap,
                Width = 320
            };

            var border = new Border
            {
                Width = 150,
                Height = 100,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = clippedText
            };

            Application.Current.MainWindow.Content = border;

            border.Measure(new Size(150, 100));
            border.Arrange(new Rect(0, 0, 150, 100));
            border.UpdateLayout();
            var elementId = elementFinder.GenerateElementId(clippedText);

            return analyzer.GetClippingInfo(elementId);
        });

        var doc = System.Text.Json.JsonSerializer.SerializeToElement(result);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("isClipped").GetBoolean().Should().BeTrue(doc.GetRawText());
    }

    [Fact]
    public void GetLayoutInfo_WithRenderTransform_ShouldReturnLayoutInfo()
    {
        // Arrange - button with RenderTransform matching TestApp Tab 6
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);

            var button = new Button
            {
                Content = "Rotated Button",
                Margin = new Thickness(5),
                RenderTransform = new RotateTransform(15)
            };

            Application.Current.MainWindow.Content = button;

            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            button.Arrange(new Rect(button.DesiredSize));

            return analyzer.GetLayoutInfo(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetLayoutInfo_WithLayoutTransform_ShouldReturnLayoutInfo()
    {
        // Arrange - button with LayoutTransform matching TestApp Tab 6
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);

            var button = new Button
            {
                Content = "Layout Rotated",
                Margin = new Thickness(5),
                LayoutTransform = new RotateTransform(45)
            };

            Application.Current.MainWindow.Content = button;

            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            button.Arrange(new Rect(button.DesiredSize));

            return analyzer.GetLayoutInfo(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetLayoutInfo_WithScaleTransform_ShouldReturnLayoutInfo()
    {
        // Arrange - button with ScaleTransform matching TestApp Tab 6
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);

            var button = new Button
            {
                Content = "Scaled Button",
                Margin = new Thickness(5),
                RenderTransform = new ScaleTransform(1.2, 1.2)
            };

            Application.Current.MainWindow.Content = button;

            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            button.Arrange(new Rect(button.DesiredSize));

            return analyzer.GetLayoutInfo(elementId: null);
        });

        result.Should().NotBeNull();
    }
}
