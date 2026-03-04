using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class StyleAnalyzerTests
{

    [StaFact]
    public void GetAppliedStyles_WithStyledElement_ShouldReturnStyle()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.WidthProperty, 100.0));
        button.Style = style;
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetAppliedStyles(elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("styles");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void GetTriggers_WithStyledElement_ShouldReturnTriggers()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var style = new Style(typeof(Button));
        button.Style = style;
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetTriggers(elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("triggers");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void GetTemplateTree_WithControl_ShouldReturnTemplateInfo()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetTemplateTree(elementId);

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
    public void GetResourceChain_WithResources_ShouldReturnChain()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        var button = new Button();
        button.Resources.Add("TestKey", "TestValue");
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetResourceChain(elementId, "TestKey");

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("chain");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void OverrideStyleSetter_WithValidSetter_ShouldOverrideValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.WidthProperty, 100.0));
        button.Style = style;

        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.OverrideStyleSetter(elementId, "Width", 200.0);

        // Assert
        result.Should().NotBeNull();
        button.Width.Should().Be(200.0);
    }
}
