using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;
using System.Windows;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class LayoutAnalyzerTests
{
    [StaFact]
    public void GetLayoutInfo_WithValidElement_ShouldReturnLayoutInfo()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var button = new Button { Width = 100, Height = 50 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetLayoutInfo(elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("layoutInfo");
            resultDict["success"].Should().Be(true);
            var layoutInfo = resultDict["layoutInfo"] as System.Collections.IDictionary;
            layoutInfo.Should().NotBeNull();
            if (layoutInfo != null)
            {
                layoutInfo.Keys.Cast<string>().Should().Contain("width");
                layoutInfo.Keys.Cast<string>().Should().Contain("height");
            }
        }
    }

    [StaFact]
    public void GetClippingInfo_WithValidElement_ShouldReturnClippingInfo()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var button = new Button { ClipToBounds = true };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetClippingInfo(elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("clippingInfo");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void InvalidateLayout_WithValidElement_ShouldInvalidateLayout()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.InvalidateLayout(elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void HighlightElement_WithValidElement_ShouldAddHighlight()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.HighlightElement(elementId, "Red", 2000);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict["success"].Should().Be(true);
        }
    }
}
