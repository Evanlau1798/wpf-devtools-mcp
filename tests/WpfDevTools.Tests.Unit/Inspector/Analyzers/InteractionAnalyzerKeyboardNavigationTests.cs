using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

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
}
