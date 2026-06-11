using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class StyleAnalyzerTests
{
    [StaFact]
    public void GetAppliedStyles_WithStyledElement_ShouldReturnStyle()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.WidthProperty, 100.0));
        button.Style = style;
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetAppliedStyles(elementId);

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
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var style = new Style(typeof(Button));
        button.Style = style;
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetTriggers(elementId);

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
    public void GetTriggers_WithControlTemplateTriggers_ShouldReturnTemplateTriggerInfo()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button
        {
            Template = BuildTemplateWithTrigger()
        };
        button.ApplyTemplate();
        var elementId = finder.GenerateElementId(button);

        var json = JsonSerializer.SerializeToElement(analyzer.GetTriggers(elementId));

        json.GetProperty("triggerCount").GetInt32().Should().Be(1);
        json.GetProperty("triggers")[0].GetProperty("source").GetString().Should().Be("ControlTemplate");
        json.GetProperty("triggers")[0].GetProperty("triggerType").GetString().Should().Be("Property");
    }

    [StaFact]
    public void GetTemplateTree_WithControl_ShouldReturnTemplateInfo()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetTemplateTree(elementId);

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
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        button.Resources.Add("TestKey", "TestValue");
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetResourceChain(elementId, "TestKey");

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
    public void OverrideStyleSetter_WithBrushValue_ShouldReturnJsonFriendlyStringValue()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty, System.Windows.Media.Brushes.Gray));
        button.Style = style;

        var elementId = finder.GenerateElementId(button);

        var result = analyzer.OverrideStyleSetter(
            elementId,
            "Background",
            JsonSerializer.SerializeToElement("LimeGreen"));
        var json = JsonSerializer.SerializeToElement(result);

        button.Background.Should().NotBeNull();
        json.GetProperty("newValue").ValueKind.Should().Be(JsonValueKind.String);
    }

    [StaFact]
    public void OverrideStyleSetter_WithValidSetter_ShouldOverrideValue()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);

        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.WidthProperty, 100.0));
        button.Style = style;

        var elementId = finder.GenerateElementId(button);

        var result = analyzer.OverrideStyleSetter(elementId, "Width", 200.0);

        result.Should().NotBeNull();
        button.Width.Should().Be(200.0);
    }

    private static ControlTemplate BuildTemplateWithTrigger()
    {
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        template.VisualTree = borderFactory;
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(UIElement.OpacityProperty, 0.8)
            }
        });
        return template;
    }
}
