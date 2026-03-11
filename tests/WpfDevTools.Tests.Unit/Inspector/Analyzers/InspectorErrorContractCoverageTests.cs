using System.Text.Json;
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

    private static void AssertStructuredError(
        object result,
        string expectedErrorCode,
        string expectedHintFragment)
    {
        var json = JsonSerializer.SerializeToElement(result);
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
