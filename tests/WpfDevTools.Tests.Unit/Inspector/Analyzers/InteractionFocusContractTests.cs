using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class InteractionFocusContractTests
{
    [StaFact]
    public void GetFocusState_ShouldReturnFocusedElementMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window();
        var first = new TextBox();
        var second = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        window.Content = panel;

        var expectedId = finder.GenerateElementId(first);
        var windowId = finder.GenerateElementId(window);
        FocusManager.SetFocusedElement(window, first);

        var result = JsonSerializer.SerializeToElement(analyzer.GetFocusState(windowId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("focusKind").GetString().Should().Be("Logical");
        result.GetProperty("focusedElementId").GetString().Should().Be(expectedId);
        result.GetProperty("windowTitle").GetString().Should().BeEmpty();
    }

    [StaFact]
    public void FocusElement_ShouldMoveLogicalFocusToTarget()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 240, Height = 180 };
        var first = new TextBox();
        var second = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        window.Content = panel;
        window.Show();

        try
        {
            FocusManager.SetFocusedElement(window, first);
            var secondId = finder.GenerateElementId(second);
            var windowId = finder.GenerateElementId(window);

            var focusResult = JsonSerializer.SerializeToElement(analyzer.FocusElement(secondId));
            var stateResult = JsonSerializer.SerializeToElement(analyzer.GetFocusState(windowId));

            focusResult.GetProperty("success").GetBoolean().Should().BeTrue();
            focusResult.GetProperty("focused").GetBoolean().Should().BeTrue();
            stateResult.GetProperty("focusedElementId").GetString().Should().Be(secondId);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FocusElement_OnInactiveTabContent_ShouldReturnElementNotLoaded()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 320, Height = 240 };
        var firstTextBox = new TextBox { Text = "Active" };
        var secondTextBox = new TextBox { Text = "Inactive" };
        var tabControl = new TabControl();
        tabControl.Items.Add(new TabItem { Header = "One", Content = firstTextBox });
        tabControl.Items.Add(new TabItem { Header = "Two", Content = secondTextBox });
        tabControl.SelectedIndex = 0;
        window.Content = tabControl;
        window.Show();

        try
        {
            var secondId = finder.GenerateElementId(secondTextBox);

            var focusResult = JsonSerializer.SerializeToElement(analyzer.FocusElement(secondId));

            focusResult.GetProperty("success").GetBoolean().Should().BeFalse();
            focusResult.GetProperty("errorCode").GetString().Should().Be("ElementNotLoaded");
            focusResult.GetProperty("hint").GetString().Should().Contain("inactive TabItem");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FocusElement_WithNonFocusableTextBox_ShouldReturnSpecificFocusabilityHint()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window { Width = 240, Height = 180 };
        var textBox = new TextBox { Focusable = false, Text = "Search" };
        window.Content = textBox;
        window.Show();

        try
        {
            var textBoxId = finder.GenerateElementId(textBox);

            var focusResult = JsonSerializer.SerializeToElement(analyzer.FocusElement(textBoxId));

            focusResult.GetProperty("success").GetBoolean().Should().BeFalse();
            focusResult.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
            focusResult.GetProperty("hint").GetString().Should().Contain("Focusable");
            focusResult.GetProperty("hint").GetString().Should().Contain("get_interaction_readiness");
            focusResult.GetProperty("hint").GetString().Should().NotContain("Choose a focusable control such as TextBox");
        }
        finally
        {
            window.Close();
        }
    }
}
