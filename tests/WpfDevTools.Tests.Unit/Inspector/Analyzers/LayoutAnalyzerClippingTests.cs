using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class LayoutAnalyzerClippingTests
{
    [StaFact]
    public void DiagnoseVisibility_WithAncestorLayoutClip_ShouldReportPartialClipping()
    {
        using var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var root = new Grid { Width = 100, Height = 40 };
        var stack = new StackPanel();
        stack.Children.Add(new Border { Height = 35 });
        var caption = new TextBlock { Text = "Clipped caption", Height = 20 };
        stack.Children.Add(caption);
        root.Children.Add(stack);
        root.Measure(new Size(100, 40));
        root.Arrange(new Rect(0, 0, 100, 40));
        root.UpdateLayout();
        VisualTreeHelper.GetClip(stack).Should().NotBeNull();
        var elementId = finder.GenerateElementId(caption);

        var result = analyzer.DiagnoseVisibility(elementId);

        var doc = JsonSerializer.SerializeToElement(result);
        doc.GetProperty("isUserVisible").GetBoolean().Should().BeTrue(doc.GetRawText());
        var clipping = doc.GetProperty("clipping");
        clipping.GetProperty("severity").GetString().Should().Be("partial");
        clipping.GetProperty("isClipped").GetBoolean().Should().BeTrue();
        clipping.GetProperty("visibleRatio").GetDouble().Should().BeInRange(0.01, 0.99);
    }

    [StaFact]
    public void GetClippingInfo_WithAncestorLayoutClip_ShouldIdentifyClippingAncestor()
    {
        using var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var root = new Grid { Width = 100, Height = 40 };
        var stack = new StackPanel();
        stack.Children.Add(new Border { Height = 35 });
        var caption = new TextBlock { Text = "Clipped caption", Height = 20 };
        stack.Children.Add(caption);
        root.Children.Add(stack);
        root.Measure(new Size(100, 40));
        root.Arrange(new Rect(0, 0, 100, 40));
        root.UpdateLayout();
        VisualTreeHelper.GetClip(stack).Should().NotBeNull(
            "WPF creates a layout clip when the child desired height exceeds its arrange slot");
        var elementId = finder.GenerateElementId(caption);

        var result = analyzer.GetClippingInfo(elementId);

        var doc = JsonSerializer.SerializeToElement(result);
        doc.GetProperty("isClipped").GetBoolean().Should().BeTrue(doc.GetRawText());
        doc.GetProperty("analysisScope").GetString().Should().Be("target-and-ancestors");
        doc.GetProperty("clippingSource").GetString().Should().Be("ancestor-layout-clip");
        var ancestor = doc.GetProperty("clippingAncestors").EnumerateArray()
            .Should().ContainSingle().Subject;
        ancestor.GetProperty("elementType").GetString().Should().Be("StackPanel");
        ancestor.GetProperty("clipSource").GetString().Should().Be("layout-clip");
        ancestor.GetProperty("overflowAmount").GetProperty("bottom").GetDouble().Should().BeGreaterThan(0);
        doc.GetProperty("suggestedFix").GetString().Should().Contain("StackPanel");
    }

    [StaFact]
    public void GetClippingInfo_WithClipToBoundsButNoActualOverflow_ShouldNotReportClipped()
    {
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var border = new Border
        {
            Width = 120,
            Height = 40,
            ClipToBounds = true,
            Child = new TextBlock { Text = "Fits" }
        };

        border.Measure(new Size(120, 40));
        border.Arrange(new Rect(0, 0, 120, 40));
        border.UpdateLayout();
        var elementId = finder.GenerateElementId(border);

        var result = analyzer.GetClippingInfo(elementId);

        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("clipToBounds").GetBoolean().Should().BeTrue();
        doc.GetProperty("hasClip").GetBoolean().Should().BeFalse();
        doc.GetProperty("isClipped").GetBoolean().Should().BeFalse(
            "ClipToBounds is a policy flag and should not imply real clipping without overflow or clip geometry");

        var overflow = doc.GetProperty("overflowAmount");
        overflow.GetProperty("left").GetDouble().Should().Be(0);
        overflow.GetProperty("top").GetDouble().Should().Be(0);
        overflow.GetProperty("right").GetDouble().Should().Be(0);
        overflow.GetProperty("bottom").GetDouble().Should().Be(0);
    }
}
