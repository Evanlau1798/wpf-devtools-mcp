using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
}
