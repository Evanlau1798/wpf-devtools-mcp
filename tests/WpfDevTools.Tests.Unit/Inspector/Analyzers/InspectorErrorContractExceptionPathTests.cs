using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class InspectorErrorContractExceptionPathTests
{
    [StaFact]
    public void TakeScreenshot_NoVisualBounds_ShouldReturnStructuredElementNotLoaded()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "base64");

        AssertStructuredError(result, "ElementNotLoaded", "rendered");
    }

    [StaFact]
    public void TakeScreenshot_ElementTooLarge_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button
        {
            Width = 5000,
            Height = 5000
        };
        button.Measure(new System.Windows.Size(5000, 5000));
        button.Arrange(new System.Windows.Rect(0, 0, 5000, 5000));
        button.UpdateLayout();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "base64");

        AssertStructuredError(result, "InvalidArgument", "smaller");
    }

    [StaFact]
    public void SimulateKeyboard_ElementNotInVisualTree_ShouldReturnStructuredElementNotLoaded()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        var result = analyzer.SimulateKeyboard(elementId, "A", "KeyDown");

        AssertStructuredError(result, "ElementNotLoaded", "visual tree");
        var json = JsonDocument.Parse(JsonSerializer.Serialize(result, result.GetType())).RootElement;
        json.GetProperty("hint").GetString().Should().Contain("TabItem");
    }

    [StaFact]
    public void GetCommands_MissingElement_ShouldReturnStructuredElementNotFound()
    {
        var analyzer = new MvvmAnalyzer(new ElementFinder());

        var result = analyzer.GetCommands("missing-element");

        AssertStructuredError(result, "ElementNotFound", "elementId");
    }

    [StaFact]
    public void ExecuteCommand_CannotExecute_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var button = new Button { DataContext = new DisabledCommandViewModel() };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.ExecuteCommand(elementId, nameof(DisabledCommandViewModel.DisabledCommand), null);

        AssertStructuredError(result, "InvalidArgument", "CanExecute");
    }

    [StaFact]
    public void ModifyViewModel_SensitiveProperty_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var button = new Button { DataContext = new SensitiveViewModel() };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.ModifyViewModel(elementId, nameof(SensitiveViewModel.ApiKey), "updated");

        AssertStructuredError(result, "InvalidArgument", "sensitive");
    }

    [StaFact]
    public void ModifyViewModel_TypeConversionFailure_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var button = new Button { DataContext = new NumericViewModel() };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.ModifyViewModel(elementId, nameof(NumericViewModel.Age), "not-a-number");

        AssertStructuredError(result, "InvalidArgument", "compatible");
    }

    [StaFact]
    public void GetResourceChain_InvalidFrameworkTarget_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var visual = new System.Windows.Media.DrawingVisual();
        var elementId = finder.GenerateElementId(visual);

        var result = analyzer.GetResourceChain(elementId, "PrimaryBrush");

        AssertStructuredError(result, "InvalidArgument", "FrameworkElement");
    }

    [StaFact]
    public void OverrideStyleSetter_InvalidFrameworkTarget_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var visual = new System.Windows.Media.DrawingVisual();
        var elementId = finder.GenerateElementId(visual);

        var result = analyzer.OverrideStyleSetter(elementId, "Width", 100);

        AssertStructuredError(result, "InvalidArgument", "FrameworkElement");
    }

    [StaFact]
    public void HighlightElement_WithoutAdornerLayer_ShouldReturnStructuredElementNotLoaded()
    {
        var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.HighlightElement(elementId, "Red", 1000);

        AssertStructuredError(result, "ElementNotLoaded", "AdornerLayer");
    }

    private static void AssertStructuredError(object result, string expectedErrorCode, string expectedHintFragment)
    {
        var json = JsonDocument.Parse(JsonSerializer.Serialize(result, result.GetType())).RootElement;
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be(expectedErrorCode);
        json.GetProperty("hint").GetString().Should().Contain(expectedHintFragment);
        json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private sealed class DisabledCommandViewModel
    {
        public ICommand DisabledCommand { get; } = new DisabledCommand();
    }

    private sealed class DisabledCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => false;

        public void Execute(object? parameter) => throw new NotSupportedException();
    }

    private sealed class SensitiveViewModel
    {
        public string ApiKey { get; set; } = "secret";
    }

    private sealed class NumericViewModel
    {
        public int Age { get; set; } = 18;
    }
}
