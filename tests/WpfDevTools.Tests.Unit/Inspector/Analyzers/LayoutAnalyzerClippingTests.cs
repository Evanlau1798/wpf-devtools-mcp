using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class LayoutAnalyzerClippingTests
{
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
