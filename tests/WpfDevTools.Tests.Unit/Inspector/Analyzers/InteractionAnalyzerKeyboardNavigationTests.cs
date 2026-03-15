using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("InteractionState")]
public sealed class InteractionAnalyzerKeyboardNavigationTests
{
    [StaFact]
    public void SimulateKeyboard_OnTextBox_WithTab_ShouldMoveFocusAndReportSemanticEffect()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 240, Height = 180 };
        var panel = new StackPanel();
        var firstTextBox = new TextBox { Text = "First" };
        var secondTextBox = new TextBox { Text = "Second" };
        panel.Children.Add(firstTextBox);
        panel.Children.Add(secondTextBox);
        window.Content = panel;
        window.Show();

        try
        {
            var firstId = finder.GenerateElementId(firstTextBox);
            var secondId = finder.GenerateElementId(secondTextBox);
            firstTextBox.Focus();
            Keyboard.Focus(firstTextBox);

            var result = JsonSerializer.SerializeToElement(analyzer.SimulateKeyboard(firstId, "Tab", "KeyDown"));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("focusChanged").GetBoolean().Should().BeTrue();
            result.GetProperty("semanticEffectObserved").GetBoolean().Should().BeTrue();
            result.GetProperty("focusedElementIdBefore").GetString().Should().Be(firstId);
            result.GetProperty("focusedElementIdAfter").GetString().Should().Be(secondId);
            secondTextBox.IsKeyboardFocused.Should().BeTrue();
            firstTextBox.Text.Should().Be("First", "Tab should move focus, not insert a tab character");
        }
        finally
        {
            window.Close();
        }
    }

    [StaTheory]
    [InlineData("Enter")]
    [InlineData("Space")]
    public void SimulateKeyboard_OnButton_WithActivationKey_ShouldReportSemanticEffect(string keyName)
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 240, Height = 180 };
        var panel = new StackPanel();
        var clicked = false;
        var button = new Button { Content = "Click Me" };
        button.Click += (_, _) => clicked = true;
        panel.Children.Add(button);
        window.Content = panel;
        window.Show();

        try
        {
            var buttonId = finder.GenerateElementId(button);
            button.Focus();
            Keyboard.Focus(button);

            var result = JsonSerializer.SerializeToElement(
                analyzer.SimulateKeyboard(buttonId, keyName, "KeyDown"));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("semanticEffectObserved").GetBoolean().Should().BeTrue(
                $"{keyName} on a Button should trigger click and report semantic effect");
            clicked.Should().BeTrue("Button click handler should have been invoked");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SimulateKeyboard_OnTextBoxDirectEdit_ShouldReportSemanticEffect()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 240, Height = 180 };
        var textBox = new TextBox();
        window.Content = textBox;
        window.Show();

        try
        {
            var textBoxId = finder.GenerateElementId(textBox);
            textBox.Focus();
            Keyboard.Focus(textBox);

            var result = JsonSerializer.SerializeToElement(
                analyzer.SimulateKeyboard(textBoxId, "A", "KeyDown"));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("appliedDirectEdit").GetBoolean().Should().BeTrue();
            result.GetProperty("semanticEffectObserved").GetBoolean().Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SimulateKeyboard_OnUnfocusedTextBoxBackspace_ShouldFocusAndDeletePreviousCharacter()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 240, Height = 180 };
        var textBox = new TextBox { Text = "25" };
        window.Content = textBox;
        window.Show();

        try
        {
            var textBoxId = finder.GenerateElementId(textBox);

            var result = JsonSerializer.SerializeToElement(
                analyzer.SimulateKeyboard(textBoxId, "Back", "KeyDown"));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("appliedDirectEdit").GetBoolean().Should().BeTrue();
            result.GetProperty("semanticEffectObserved").GetBoolean().Should().BeTrue();
            result.GetProperty("focusChanged").GetBoolean().Should().BeTrue();
            textBox.Text.Should().Be("2");
            textBox.IsKeyboardFocused.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SimulateKeyboard_WhenKeyHandlerMovesFocus_ShouldReportFocusChange()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 240, Height = 180 };
        var panel = new StackPanel();
        var firstTextBox = new TextBox { Text = "First" };
        var secondTextBox = new TextBox { Text = "Second" };
        firstTextBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            secondTextBox.Focus();
            Keyboard.Focus(secondTextBox);
            e.Handled = true;
        };
        panel.Children.Add(firstTextBox);
        panel.Children.Add(secondTextBox);
        window.Content = panel;
        window.Show();

        try
        {
            var firstId = finder.GenerateElementId(firstTextBox);
            var secondId = finder.GenerateElementId(secondTextBox);
            firstTextBox.Focus();
            Keyboard.Focus(firstTextBox);

            var result = JsonSerializer.SerializeToElement(
                analyzer.SimulateKeyboard(firstId, "Enter", "KeyDown"));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("focusChanged").GetBoolean().Should().BeTrue();
            result.GetProperty("semanticEffectObserved").GetBoolean().Should().BeTrue();
            result.GetProperty("focusedElementIdBefore").GetString().Should().Be(firstId);
            result.GetProperty("focusedElementIdAfter").GetString().Should().Be(secondId);
        }
        finally
        {
            window.Close();
        }
    }
}
