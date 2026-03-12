using System.Text.Json;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class LayoutAnalyzerSchemaContractTests
{
    [StaFact]
    public void GetLayoutInfo_ShouldExposeDocumentedAlignmentPaddingAndPositions()
    {
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var button = new Button
        {
            Width = 120,
            Height = 40,
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetLayoutInfo(elementId);
        var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("padding", out _).Should().BeTrue();
        doc.GetProperty("horizontalAlignment").GetString().Should().Be(nameof(HorizontalAlignment.Center));
        doc.GetProperty("verticalAlignment").GetString().Should().Be(nameof(VerticalAlignment.Stretch));
        doc.TryGetProperty("positionInParent", out _).Should().BeTrue();
        doc.TryGetProperty("positionInWindow", out _).Should().BeTrue();
    }

    [StaFact]
    public void GetClippingInfo_ShouldExposeDocumentedIsClippedAndOverflowShape()
    {
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var border = new Border
        {
            Width = 50,
            Height = 20,
            ClipToBounds = true,
            Child = new TextBlock { Text = "This text is longer than the container" }
        };
        var elementId = finder.GenerateElementId(border);

        var result = analyzer.GetClippingInfo(elementId);
        var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("isClipped", out _).Should().BeTrue();
        doc.TryGetProperty("overflowAmount", out _).Should().BeTrue();
    }

    [StaFact]
    public void GetLayoutInfo_OnInactiveTabContent_ShouldExposeNotRenderedSemantics()
    {
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var textBox = new TextBox { Name = "InactiveTextBox", Width = 120 };
        var tabControl = new TabControl
        {
            Items =
            {
                new TabItem
                {
                    Header = "Active",
                    Content = new TextBlock { Text = "Visible" }
                },
                new TabItem
                {
                    Header = "Inactive",
                    Content = textBox
                }
            },
            SelectedIndex = 0
        };

        var window = new Window { Content = tabControl };
        window.Show();
        window.UpdateLayout();

        try
        {
            var elementId = finder.GenerateElementId(textBox);
            var result = analyzer.GetLayoutInfo(elementId);
            var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

            doc.GetProperty("layoutState").GetString().Should().Be("NotRendered");
            doc.GetProperty("notRenderedReason").GetString().Should().Be("ElementInInactiveTab");
        }
        finally
        {
            window.Close();
        }
    }
}
