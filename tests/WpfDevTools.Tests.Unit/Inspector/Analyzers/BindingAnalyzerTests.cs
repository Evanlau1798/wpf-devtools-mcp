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
}
