using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class LayoutAnalyzerVisibilityTests
{
    [StaFact]
    public void DiagnoseVisibility_WhenAncestorIsCollapsed_ShouldReportAncestorRootCause()
    {
        var finder = new ElementFinder();
        var root = new Window();
        var parent = new StackPanel { Name = "HiddenByAncestorPanel", Visibility = Visibility.Collapsed };
        var child = new TextBlock { Name = "HiddenByAncestorText", Text = "Hidden" };
        parent.Children.Add(child);
        root.Content = parent;
        root.Show();
        try
        {
            var analyzer = new LayoutAnalyzer(finder);
            var childId = finder.GenerateElementId(child);

            var result = JsonSerializer.SerializeToElement(analyzer.DiagnoseVisibility(childId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
            result.GetProperty("rootCause").GetString().Should().Contain("HiddenByAncestorPanel");
        }
        finally
        {
            root.Close();
        }
    }

    [StaFact]
    public void DiagnoseVisibility_WhenElementIsFullyClipped_ShouldReportClippingRootCause()
    {
        var finder = new ElementFinder();
        var root = new Window();
        var border = new Border
        {
            Width = 100,
            Height = 50,
            ClipToBounds = true,
            Child = new Canvas
            {
                Width = 220,
                Height = 50,
                Children =
                {
                    new Button
                    {
                        Name = "ClippingButtonSample",
                        Content = "Outside",
                        Width = 80,
                        Height = 30
                    }
                }
            }
        };
        var button = (Button)((Canvas)border.Child).Children[0];
        Canvas.SetLeft(button, 140);
        root.Content = border;
        root.Show();
        root.UpdateLayout();
        try
        {
            var analyzer = new LayoutAnalyzer(finder);
            var elementId = finder.GenerateElementId(button);

            var result = JsonSerializer.SerializeToElement(analyzer.DiagnoseVisibility(elementId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
            result.GetProperty("rootCause").GetString().Should().Contain("fully clipped");
        }
        finally
        {
            root.Close();
        }
    }

    [StaFact]
    public void DiagnoseVisibility_WhenElementIsPartiallyClipped_ShouldRemainUserVisible()
    {
        var finder = new ElementFinder();
        var root = new Window();
        var border = new Border
        {
            Width = 100,
            Height = 50,
            ClipToBounds = true,
            Child = new Canvas
            {
                Width = 160,
                Height = 50,
                Children =
                {
                    new Button
                    {
                        Name = "PartiallyClippedButtonSample",
                        Content = "Partial",
                        Width = 80,
                        Height = 30
                    }
                }
            }
        };
        var button = (Button)((Canvas)border.Child).Children[0];
        Canvas.SetLeft(button, 60);
        root.Content = border;
        root.Show();
        root.UpdateLayout();
        try
        {
            var analyzer = new LayoutAnalyzer(finder);
            var elementId = finder.GenerateElementId(button);

            var result = JsonSerializer.SerializeToElement(analyzer.DiagnoseVisibility(elementId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("isUserVisible").GetBoolean().Should().BeTrue(result.GetRawText());
            result.GetProperty("rootCause").ValueKind.Should().Be(JsonValueKind.Null);
            result.GetProperty("clipping").GetProperty("severity").GetString().Should().Be("partial");
            result.GetProperty("clipping").GetProperty("visibleRatio").GetDouble()
                .Should().BeGreaterThan(0).And.BeLessThan(1);
        }
        finally
        {
            root.Close();
        }
    }

    [StaFact]
    public void DiagnoseVisibility_WhenTranslateTransformMovesElementOffscreen_ShouldReportRenderTransformRootCause()
    {
        // Task 4 acceptance case: TranslateTransform displacement must be diagnosed explicitly.
        var finder = new ElementFinder();
        var button = new Button
        {
            Name = "OffscreenTranslatedButton",
            Content = "Translated Offscreen",
            Width = 120,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform(520, 0)
        };
        var root = new Window
        {
            Width = 320,
            Height = 200,
            Content = new Grid
            {
                Children =
                {
                    button
                }
            }
        };

        root.Show();
        root.UpdateLayout();
        try
        {
            var analyzer = new LayoutAnalyzer(finder);
            var elementId = finder.GenerateElementId(button);

            var result = JsonSerializer.SerializeToElement(analyzer.DiagnoseVisibility(elementId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
            result.GetProperty("rootCause").GetString().Should().Contain("RenderTransform");
            result.GetProperty("suggestedFix").GetString().Should().Contain("RenderTransform");
        }
        finally
        {
            root.Close();
        }
    }
}
