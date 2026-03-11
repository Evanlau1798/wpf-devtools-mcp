using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class InspectorErrorContractCoverageTests
{
    [StaFact]
    public void ClickElement_NonClickableElement_ShouldReturnStructuredError()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        var result = analyzer.ClickElement(elementId);

        AssertStructuredError(
            result,
            "ElementNotClickable",
            expectedHintFragment: "ButtonBase");
    }

    [StaFact]
    public void ExecuteCommand_MissingCommand_ShouldReturnStructuredError()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var host = new Button { DataContext = new CommandlessViewModel() };
        var elementId = finder.GenerateElementId(host);

        var result = analyzer.ExecuteCommand(elementId, "MissingCommand", parameter: null);

        AssertStructuredError(
            result,
            "CommandNotFound",
            expectedHintFragment: "get_commands");
    }

    [StaFact]
    public void GetValueSource_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new DependencyPropertyAnalyzer(new ElementFinder());

        var result = analyzer.GetValueSource("Width", "missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetBindings_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new BindingAnalyzer(new ElementFinder());

        var result = analyzer.GetBindings("missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void FireRoutedEvent_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new EventAnalyzer(new ElementFinder());

        var result = analyzer.FireRoutedEvent("missing-element", "Click", eventArgs: null);

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void SimulateKeyboard_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new InteractionAnalyzer(new ElementFinder());

        var result = analyzer.SimulateKeyboard("missing-element", "Enter", "KeyDown");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void FocusElement_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new InteractionAnalyzer(new ElementFinder());

        var result = analyzer.FocusElement("missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetAppliedStyles_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new StyleAnalyzer(new ElementFinder());

        var result = analyzer.GetAppliedStyles("missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetResourceChain_EmptyKey_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetResourceChain(elementId, "");

        AssertStructuredError(
            result,
            "InvalidArgument",
            expectedHintFragment: "resource key");
    }

    [StaFact]
    public void OverrideStyleSetter_MissingProperty_ShouldReturnStructuredPropertyNotFound()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.OverrideStyleSetter(elementId, "MissingProperty", 100);

        AssertStructuredError(
            result,
            "PropertyNotFound",
            expectedHintFragment: "propertyName");
    }

    [StaFact]
    public void GetLayoutInfo_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new LayoutAnalyzer(new ElementFinder());

        var result = analyzer.GetLayoutInfo("missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetLogicalTree_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new LogicalTreeAnalyzer(new ElementFinder());

        var result = analyzer.GetLogicalTree(null, "missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetVisualCount_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new PerformanceAnalyzer(new ElementFinder());

        var result = analyzer.GetVisualCount("missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetVisualTree_MissingRoot_ShouldReturnStructuredError()
    {
        var analyzer = new VisualTreeAnalyzer(new ElementFinder());

        var result = analyzer.GetVisualTree();

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void CompareTree_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new VisualTreeAnalyzer(new ElementFinder());

        var result = analyzer.CompareTree("missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetNameScope_MissingElement_ShouldReturnStructuredError()
    {
        var analyzer = new VisualTreeAnalyzer(new ElementFinder());

        var result = analyzer.GetNameScope("missing-element");

        AssertStructuredError(
            result,
            "ElementNotFound",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetTemplateTree_MissingElementId_ShouldReturnStructuredInvalidArgument()
    {
        var analyzer = new VisualTreeAnalyzer(new ElementFinder());

        var result = analyzer.GetTemplateTree(null);

        AssertStructuredError(
            result,
            "InvalidArgument",
            expectedHintFragment: "elementId");
    }

    [StaFact]
    public void GetTemplateTree_NonTemplatedElement_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var stackPanel = new StackPanel();
        var elementId = finder.GenerateElementId(stackPanel);

        var result = analyzer.GetTemplateTree(elementId);

        AssertStructuredError(
            result,
            "InvalidArgument",
            expectedHintFragment: "templated control");
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

        AssertStructuredError(
            result,
            "ElementNotLoaded",
            expectedHintFragment: "loaded");
    }

    private static void AssertStructuredError(
        object result,
        string expectedErrorCode,
        string expectedHintFragment)
    {
        var json = JsonSerializer.SerializeToElement(result, result.GetType());
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be(expectedErrorCode);
        json.GetProperty("hint").GetString().Should().Contain(expectedHintFragment);
        json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private sealed class CommandlessViewModel
    {
        public string Name { get; set; } = "Test";
    }
}
