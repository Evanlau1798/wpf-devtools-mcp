using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Text.Json;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("InteractionState")]
public class InteractionAnalyzerTests
{

    [StaFact]
    public void ClickElement_WithValidButton_ShouldRaiseClickEvent()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var clicked = false;
        button.Click += (s, e) => clicked = true;

        // Act
        var result = analyzer.ClickElement(elementId);

        // Assert
        clicked.Should().BeTrue();
        result.Should().NotBeNull();
    }


    [StaFact]
    public void ClickElement_WithButtonCommand_ShouldExecuteCommand()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var executed = false;
        var button = new Button
        {
            Command = new TestCommand(() => executed = true)
        };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ClickElement(elementId);

        // Assert
        executed.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [StaFact]
    public void ClickElement_WithTabItem_ShouldSelectTab()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var firstTab = new TabItem { Header = "First", Content = new TextBlock { Text = "First" } };
        var secondTab = new TabItem { Header = "Second", Content = new TextBlock { Text = "Second" } };
        var tabControl = new TabControl();
        tabControl.Items.Add(firstTab);
        tabControl.Items.Add(secondTab);
        tabControl.SelectedItem = firstTab;

        var elementId = finder.GenerateElementId(secondTab);

        var result = analyzer.ClickElement(elementId);
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(result);

        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
        tabControl.SelectedItem.Should().BeSameAs(secondTab);
        secondTab.IsSelected.Should().BeTrue();
    }

    [StaFact]
    public void ClickElement_WithButtonCommand_ShouldExecuteCommandExactlyOnce()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var executeCount = 0;
        var button = new Button
        {
            Command = new TestCommand(() => executeCount++)
        };
        var elementId = finder.GenerateElementId(button);

        analyzer.ClickElement(elementId);

        executeCount.Should().Be(1, "command should execute exactly once, not twice");
    }

    [StaFact]
    public void ScrollToElement_WithValidElement_ShouldBringIntoView()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ScrollToElement(elementId);

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void TakeScreenshot_WithValidElement_ShouldReturnMetadataByDefault()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 100, Height = 50 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.TakeScreenshot(elementId);

        // Assert
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.TryGetProperty("base64Image", out _).Should().BeFalse();
        json.TryGetProperty("imageData", out _).Should().BeFalse();
        json.GetProperty("rendered").GetBoolean().Should().BeFalse();
        json.GetProperty("byteLength").GetInt32().Should().Be(0);
        json.GetProperty("width").GetInt32().Should().Be(100);
        json.GetProperty("height").GetInt32().Should().Be(50);
    }

    [StaFact]
    public void DragAndDrop_WithValidElements_ShouldSimulateDrag()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var source = new Button { Content = "Source" };
        var target = new Button { Content = "Target" };
        var sourceId = finder.GenerateElementId(source);
        var targetId = finder.GenerateElementId(target);

        // Act
        var result = analyzer.DragAndDrop(sourceId, targetId, "Text");

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void SimulateKeyboard_WithValidElement_ShouldSimulateKeyPress()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        // Act
        var result = analyzer.SimulateKeyboard(elementId, "A", "KeyDown");

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void SimulateKeyboard_OnCheckBox_WithSpace_ShouldToggleIsChecked()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var checkBox = new CheckBox { IsChecked = false };
        window.Content = checkBox;
        window.Show();

        try
        {
            var elementId = finder.GenerateElementId(checkBox);
            var result = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(analyzer.SimulateKeyboard(elementId, "Space", "KeyDown")));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            checkBox.IsChecked.Should().BeTrue("Space key on CheckBox should toggle IsChecked");
        }
        finally { window.Close(); }
    }

    [StaFact]
    public void SimulateKeyboard_OnComboBox_WithDown_ShouldChangeSelection()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var comboBox = new ComboBox();
        comboBox.Items.Add("Item 0");
        comboBox.Items.Add("Item 1");
        comboBox.Items.Add("Item 2");
        comboBox.SelectedIndex = 0;
        window.Content = comboBox;
        window.Show();

        try
        {
            var elementId = finder.GenerateElementId(comboBox);
            var result = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(analyzer.SimulateKeyboard(elementId, "Down", "KeyDown")));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            comboBox.SelectedIndex.Should().Be(1, "Down key on ComboBox should move selection down");
        }
        finally { window.Close(); }
    }

    [StaFact]
    public void SimulateKeyboard_OnTextBox_WithLetterKey_ShouldInsertCharacter()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var textBox = new TextBox { Text = "" };
        window.Content = textBox;
        window.Show();

        try
        {
            var elementId = finder.GenerateElementId(textBox);
            var result = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(analyzer.SimulateKeyboard(elementId, "A", "KeyDown")));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("appliedDirectEdit").GetBoolean().Should().BeTrue();
            textBox.Text.Should().Be("a");
        }
        finally { window.Close(); }
    }

    [StaFact]
    public void SimulateKeyboard_OnTextBox_WithDigitKey_ShouldInsertDigit()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var textBox = new TextBox { Text = "test" };
        window.Content = textBox;
        window.Show();

        try
        {
            var elementId = finder.GenerateElementId(textBox);
            textBox.Focus();
            textBox.CaretIndex = textBox.Text.Length;

            var result = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(analyzer.SimulateKeyboard(elementId, "D5", "KeyDown")));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("appliedDirectEdit").GetBoolean().Should().BeTrue();
            textBox.Text.Should().Be("test5");
        }
        finally { window.Close(); }
    }

    [StaFact]
    public void SimulateKeyboard_OnTextBox_WithMultipleKeys_ShouldBuildString()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var textBox = new TextBox { Text = "" };
        window.Content = textBox;
        window.Show();

        try
        {
            var elementId = finder.GenerateElementId(textBox);
            analyzer.SimulateKeyboard(elementId, "H", "KeyDown");
            analyzer.SimulateKeyboard(elementId, "I", "KeyDown");

            textBox.Text.Should().Be("hi");
        }
        finally { window.Close(); }
    }

    [StaFact]
    public void SimulateKeyboard_OnReadOnlyTextBox_WithLetterKey_ShouldNotInsert()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var textBox = new TextBox { Text = "original", IsReadOnly = true };
        window.Content = textBox;
        window.Show();

        try
        {
            var elementId = finder.GenerateElementId(textBox);
            analyzer.SimulateKeyboard(elementId, "A", "KeyDown");

            textBox.Text.Should().Be("original", "ReadOnly TextBox should not accept character input");
        }
        finally { window.Close(); }
    }

    [StaFact]
    public void SimulateKeyboard_OnTextBox_WithSpaceKey_ShouldInsertSpace()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var textBox = new TextBox { Text = "ab" };
        window.Content = textBox;
        window.Show();

        try
        {
            var elementId = finder.GenerateElementId(textBox);
            textBox.Focus();
            textBox.CaretIndex = 1;

            var result = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(analyzer.SimulateKeyboard(elementId, "Space", "KeyDown")));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("appliedDirectEdit").GetBoolean().Should().BeTrue();
            textBox.Text.Should().Be("a b");
        }
        finally { window.Close(); }
    }

    [StaFact]
    public void SimulateKeyboard_ShouldRaiseBothPreviewAndBubbleEvents()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 200, Height = 200 };
        var button = new Button();
        window.Content = button;
        window.Show();

        bool previewFired = false;
        bool keyDownFired = false;
        button.PreviewKeyDown += (s, e) => previewFired = true;
        button.KeyDown += (s, e) => keyDownFired = true;

        try
        {
            var elementId = finder.GenerateElementId(button);
            analyzer.SimulateKeyboard(elementId, "A", "KeyDown");

            previewFired.Should().BeTrue("PreviewKeyDown tunnel event should fire");
            keyDownFired.Should().BeTrue("KeyDown bubble event should fire");
        }
        finally { window.Close(); }
    }

    [StaFact]
    public void GetInteractionReadiness_WithBoundButtonCommand_ShouldExposeRedactedCommandReadiness()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var viewModel = new CommandReadinessViewModel();
        var button = new Button
        {
            DataContext = viewModel,
            CommandParameter = "private-command-parameter"
        };
        button.SetBinding(ButtonBase.CommandProperty, new Binding(nameof(CommandReadinessViewModel.SaveCommand)));
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetInteractionReadiness(elementId, "Click")));

        var commandReadiness = result.GetProperty("commandReadiness");
        commandReadiness.GetProperty("hasCommand").GetBoolean().Should().BeTrue();
        commandReadiness.GetProperty("sourceElementId").GetString().Should().Be(elementId);
        commandReadiness.GetProperty("sourceElementType").GetString().Should().Be(nameof(Button));
        commandReadiness.GetProperty("commandName").GetString().Should().Be(nameof(CommandReadinessViewModel.SaveCommand));
        commandReadiness.GetProperty("commandNameSource").GetString().Should().Be("BindingPath");
        commandReadiness.GetProperty("canExecute").GetBoolean().Should().BeTrue();
        commandReadiness.GetProperty("commandParameterKind").GetString().Should().Be("String");
        commandReadiness.GetProperty("riskNotes").EnumerateArray()
            .Select(note => note.GetString())
            .Should().Contain("CommandParameterValueRedacted");
        result.GetRawText().Should().NotContain("private-command-parameter");
    }

    private sealed class TestCommand : ICommand
    {
        private readonly Action _execute;

        public TestCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }

    private sealed class CommandReadinessViewModel
    {
        public ICommand SaveCommand { get; } = new TestCommand(() => { });
    }
}
