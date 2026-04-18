using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class BindingAnalyzerAdvancedTests
{
    [StaFact]
    public void GetBindingValueChain_WithNoBinding_ShouldReturnHasBindingFalse()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var button = new Button { Width = 100 };

        // Act
        dynamic result = analyzer.GetBindingValueChain(button, "Width");

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((bool)result.hasBinding).Should().BeFalse();
    }

    [StaFact]
    public void GetBindingValueChain_WithConverter_ShouldIncludeConverterInfo()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var button = new Button { Width = 100 };
        var binding = new Binding("Width")
        {
            Source = button,
            Converter = new TestConverter()
        };
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, binding);

        // Act
        dynamic result = analyzer.GetBindingValueChain(textBox, "Text");

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((bool)result.hasBinding).Should().BeTrue();
        Assert.NotNull(result.chain);
        var chain = (IEnumerable<object>)result.chain;
        var chainList = chain.ToList();

        // Find the binding step
        var bindingStep = chainList.FirstOrDefault(item =>
        {
            var dict = item as System.Collections.IDictionary;
            return dict?["step"]?.ToString() == "Binding";
        });

        Assert.NotNull(bindingStep);
        var bindingDict = bindingStep as System.Collections.IDictionary;
        Assert.Equal("TestConverter", bindingDict?["converter"]?.ToString());
    }

    [StaFact]
    public void GetBindingValueChain_WithFallbackValue_ShouldReturnChain()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var binding = new Binding("NonExistentPath")
        {
            FallbackValue = "Fallback",
            Mode = BindingMode.OneWay
        };
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, binding);

        // Act
        dynamic result = analyzer.GetBindingValueChain(textBox, "Text");

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((bool)result.hasBinding).Should().BeTrue();
    }

    [StaFact]
    public void GetBindingValueChain_WithNullElement_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();

        // Act
        dynamic result = analyzer.GetBindingValueChain((DependencyObject)null!, "Text");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("Element not found");
    }

    [StaFact]
    public void GetBindingValueChain_WithEmptyPropertyName_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var button = new Button();

        // Act
        dynamic result = analyzer.GetBindingValueChain(button, "");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("propertyName is required");
    }

    [StaFact]
    public void GetBindingValueChain_WithNonExistentProperty_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var button = new Button();

        // Act
        dynamic result = analyzer.GetBindingValueChain(button, "NonExistentProperty");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("not found");
    }

    [StaFact]
    public void ForceBindingUpdate_WithUpdateTarget_ShouldUpdateTarget()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var viewModel = new TestViewModel { Value = "Original" };
        var binding = new Binding("Value") { Source = viewModel, Mode = BindingMode.TwoWay };
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, binding);

        // Change source value
        viewModel.Value = "Updated";

        // Act
        dynamic result = analyzer.ForceBindingUpdate(textBox, "Text", "Target");

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((string)result.direction).Should().Be("Target");
        textBox.Text.Should().Be("Updated");
    }

    [StaFact]
    public void ForceBindingUpdate_WithInvalidDirection_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var viewModel = new TestViewModel { Value = "Test" };
        var binding = new Binding("Value") { Source = viewModel };
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, binding);

        // Act
        dynamic result = analyzer.ForceBindingUpdate(textBox, "Text", "Invalid");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("Invalid direction");
    }

    [StaFact]
    public void ForceBindingUpdate_WithNoBinding_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var textBox = new TextBox { Text = "Test" };

        // Act
        dynamic result = analyzer.ForceBindingUpdate(textBox, "Text", "Source");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("No binding");
    }

    [StaFact]
    public void ForceBindingUpdate_WithNullElement_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();

        // Act
        dynamic result = analyzer.ForceBindingUpdate((DependencyObject)null!, "Text", "Source");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("Element not found");
    }

    [StaFact]
    public void ForceBindingUpdate_WithEmptyPropertyName_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();
        var textBox = new TextBox();

        // Act
        dynamic result = analyzer.ForceBindingUpdate(textBox, "", "Source");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("propertyName is required");
    }

    [StaFact]
    public void GetDataContextChain_WithMultipleLevels_ShouldReturnAllLevels()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var rootViewModel = new { Name = "Root" };
        var childViewModel = new { Name = "Child" };
        var stackPanel = new StackPanel { DataContext = rootViewModel };
        var button = new Button { DataContext = childViewModel };
        stackPanel.Children.Add(button);
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetDataContextChain(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var chain = (IEnumerable<object>)result.chain;
        chain.Count().Should().BeGreaterThanOrEqualTo(1);
    }

    [StaFact]
    public void GetDataContextChain_WithNoDataContext_ShouldReturnChain()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetDataContextChain(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var chain = (IEnumerable<object>)result.chain;
        chain.Count().Should().BeGreaterThanOrEqualTo(1);
    }

    [StaFact]
    public void GetBindingValueChain_ByElementId_WithValidElement_ShouldReturnChain()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button { Width = 100 };
        var binding = new Binding("Width") { Source = button };
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, binding);
        var elementId = finder.GenerateElementId(textBox);

        // Act
        dynamic result = analyzer.GetBindingValueChain(elementId, "Text");

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((bool)result.hasBinding).Should().BeTrue();
    }

    [Fact]
    public void GetBindingValueChain_ByElementId_WithInvalidElement_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();

        // Act
        dynamic result = analyzer.GetBindingValueChain("InvalidId", "Text");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("not found");
    }

    [StaFact]
    public void ForceBindingUpdate_ByElementId_WithValidElement_ShouldUpdate()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var viewModel = new TestViewModel { Value = "Original" };
        var binding = new Binding("Value") { Source = viewModel, Mode = BindingMode.TwoWay };
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, binding);
        textBox.Text = "Modified";
        var elementId = finder.GenerateElementId(textBox);

        // Act
        dynamic result = analyzer.ForceBindingUpdate(elementId, "Text", "Source");

        // Assert
        ((bool)result.success).Should().BeTrue();
        viewModel.Value.Should().Be("Modified");
    }

    [Fact]
    public void ForceBindingUpdate_ByElementId_WithInvalidElement_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();

        // Act
        dynamic result = analyzer.ForceBindingUpdate("InvalidId", "Text", "Source");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("not found");
    }

    private class TestViewModel
    {
        public string Value { get; set; } = string.Empty;
    }

    private class TestConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value;
        }
    }
}
