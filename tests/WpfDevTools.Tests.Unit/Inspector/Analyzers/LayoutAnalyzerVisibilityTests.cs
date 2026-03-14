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
    public void DiagnoseVisibility_WhenElementIsClipped_ShouldReportClippingRootCause()
    {
        var finder = new ElementFinder();
        var root = new Window();
        var border = new Border
        {
            Width = 150,
            Height = 50,
            ClipToBounds = true,
            Child = new TextBlock
            {
                Name = "ClippingTextSample",
                Text = "This text is very long and will be clipped by the border because ClipToBounds is True",
                TextWrapping = TextWrapping.Wrap
            }
        };
        root.Content = border;
        root.Show();
        root.UpdateLayout();
        try
        {
            var analyzer = new LayoutAnalyzer(finder);
            var elementId = finder.GenerateElementId((TextBlock)border.Child);

            var result = JsonSerializer.SerializeToElement(analyzer.DiagnoseVisibility(elementId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
            result.GetProperty("rootCause").GetString().Should().Contain("clipped");
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
