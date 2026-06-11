using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class InspectorErrorContractTreeCoverageTests
{
    [StaFact]
    public void GetVisualTree_MissingRoot_ShouldReturnStructuredError()
    {
        var analyzer = new VisualTreeAnalyzer(new ElementFinder());

        var result = analyzer.GetVisualTree();

        AssertStructuredError(result, "ElementNotFound", "elementId");
    }

    [StaFact]
    public void CompareTree_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new VisualTreeAnalyzer(new ElementFinder());

        var result = analyzer.CompareTree("missing-element");

        AssertStructuredError(result, "ElementNotFound", "elementId");
    }

    [StaFact]
    public void GetNameScope_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new VisualTreeAnalyzer(new ElementFinder());

        var result = analyzer.GetNameScope("missing-element");

        AssertStructuredError(result, "ElementNotFound", "elementId");
    }

    [StaFact]
    public void GetTemplateTree_MissingElementId_ShouldReturnStructuredInvalidArgument()
    {
        var analyzer = new VisualTreeAnalyzer(new ElementFinder());

        var result = analyzer.GetTemplateTree(null);

        AssertStructuredError(result, "InvalidArgument", "elementId");
    }

    [StaFact]
    public void GetTemplateTree_NonTemplatedElement_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var stackPanel = new StackPanel();
        var elementId = finder.GenerateElementId(stackPanel);

        var result = analyzer.GetTemplateTree(elementId);

        AssertStructuredError(result, "InvalidArgument", "templated control");
    }

    [StaFact]
    public void GetTemplateTree_TemplateWithoutVisualTree_ShouldReturnStructuredElementNotLoaded()
    {
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var button = new Button
        {
            Template = new ControlTemplate(typeof(Button))
            {
                VisualTree = new FrameworkElementFactory(typeof(Border))
            }
        };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetTemplateTree(elementId);

        AssertStructuredError(result, "ElementNotLoaded", "loaded");
    }

    private static void AssertStructuredError(object result, string expectedErrorCode, string expectedHintFragment)
    {
        var json = JsonDocument.Parse(JsonSerializer.Serialize(result, result.GetType())).RootElement;
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be(expectedErrorCode);
        json.GetProperty("hint").GetString().Should().Contain(expectedHintFragment);
        json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
