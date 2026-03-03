using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class BindingAnalyzerTests
{
    [StaFact]
    public void GetBindingValueChain_WithBinding_ShouldReturnChain()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var button = new Button { Width = 100 };
        var binding = new Binding("Width") { Source = button };
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, binding);

        // Act
        var result = analyzer.GetBindingValueChain(textBox, "Text");

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void ForceBindingUpdate_WithBinding_ShouldUpdateSource()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var viewModel = new TestViewModel { Value = "Original" };
        var binding = new Binding("Value") { Source = viewModel, Mode = BindingMode.TwoWay };
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, binding);
        textBox.Text = "Modified";

        // Act
        var result = analyzer.ForceBindingUpdate(textBox, "Text", "Source");

        // Assert
        result.Should().NotBeNull();
        viewModel.Value.Should().Be("Modified");
    }

    private class TestViewModel
    {
        public string Value { get; set; } = string.Empty;
    }
}
