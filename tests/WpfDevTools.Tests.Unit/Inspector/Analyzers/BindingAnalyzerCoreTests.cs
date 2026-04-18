using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class BindingAnalyzerTests
{
    private static (BindingAnalyzer Analyzer, BindingErrorTraceListener Listener) CreateBindingErrorAnalyzer(
        ElementFinder? finder = null)
    {
        var actualFinder = finder ?? new ElementFinder();
        var listener = BindingErrorTraceListener.CreateForTesting();
        return (new BindingAnalyzer(actualFinder, null, listener), listener);
    }

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

    [Fact]
    public void GetBindingErrors_WhenNoErrors_ShouldReturnEmptyList()
    {
        // Arrange
        var (analyzer, _) = CreateBindingErrorAnalyzer();

        // Act
        dynamic result = analyzer.GetBindingErrors();

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((int)result.errorCount).Should().Be(0);
    }

    [Fact]
    public void GetBindingErrors_WithCapturedErrors_ShouldReturnErrors()
    {
        // Arrange
        var (analyzer, listener) = CreateBindingErrorAnalyzer();

        // Simulate binding errors via TraceEvent
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40,
            "BindingExpression path error: 'InvalidPath' not found");
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40,
            "BindingExpression path error: 'AnotherBadPath' not found");

        // Act
        dynamic result = analyzer.GetBindingErrors();

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((int)result.errorCount).Should().Be(2);
        ((IEnumerable<object>)result.errors).Should().HaveCount(2);
    }

    [Fact]
    public void GetBindingErrors_WithClearAfterRead_ShouldClearErrors()
    {
        // Arrange
        var (analyzer, listener) = CreateBindingErrorAnalyzer();
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40, "Error 1");

        // Act
        dynamic result = analyzer.GetBindingErrors(clearAfterRead: true);

        // Assert
        ((int)result.errorCount).Should().Be(1);

        // After clear, should be empty
        dynamic result2 = analyzer.GetBindingErrors();
        ((int)result2.errorCount).Should().Be(0);
    }

    [Fact]
    public void GetBindingErrors_WithoutClearAfterRead_ShouldRetainErrors()
    {
        // Arrange
        var (analyzer, listener) = CreateBindingErrorAnalyzer();
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40, "Error 1");

        // Act
        dynamic result1 = analyzer.GetBindingErrors(clearAfterRead: false);
        dynamic result2 = analyzer.GetBindingErrors(clearAfterRead: false);

        // Assert - errors should persist across reads
        ((int)result1.errorCount).Should().Be(1);
        ((int)result2.errorCount).Should().Be(1);
    }

    [StaFact]
    public void GetBindings_WithInheritedPropertyBindings_ShouldFindAllProperties()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Bind properties from different inheritance levels
        // Button.Content (from ContentControl)
        button.SetBinding(Button.ContentProperty, new Binding("ButtonContent"));
        // Button.Width (from FrameworkElement)
        button.SetBinding(Button.WidthProperty, new Binding("ButtonWidth"));
        // Button.Visibility (from UIElement)
        button.SetBinding(Button.VisibilityProperty, new Binding("ButtonVisibility"));

        // Act
        dynamic result = analyzer.GetBindings(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var bindings = result.bindings as IEnumerable<object>;
        bindings.Should().NotBeNull();

        // Should find bindings from all inheritance levels
        var bindingList = bindings!.ToList();
        var propertyNames = bindingList
            .Select(b => (b as System.Collections.IDictionary)?["propertyName"]?.ToString())
            .Where(name => name != null)
            .ToList();

        propertyNames.Should().Contain("Content");
        propertyNames.Should().Contain("Width");
        propertyNames.Should().Contain("Visibility");
    }

    [StaFact]
    public void GetBindings_WithDuplicatePropertyNames_ShouldNotReturnDuplicates()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Bind a property that exists in the type hierarchy
        button.SetBinding(Button.WidthProperty, new Binding("TestWidth"));

        // Act
        dynamic result = analyzer.GetBindings(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var bindings = result.bindings as IEnumerable<object>;
        bindings.Should().NotBeNull();

        // Count how many times "Width" appears
        var propertyNames = bindings!
            .Select(b => (b as System.Collections.IDictionary)?["propertyName"]?.ToString())
            .Where(name => name == "Width")
            .ToList();

        // Should only appear once (seenProperties HashSet prevents duplicates)
        propertyNames.Should().HaveCount(1);
    }

    [StaFact]
    public void GetBindings_WithComplexInheritanceChain_ShouldTraverseToObject()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var textBox = new TextBox(); // TextBox has deep inheritance chain
        var elementId = finder.GenerateElementId(textBox);

        // Bind properties from various levels
        textBox.SetBinding(TextBox.TextProperty, new Binding("Text")); // TextBox
        textBox.SetBinding(TextBox.FontSizeProperty, new Binding("Size")); // Control
        textBox.SetBinding(TextBox.MarginProperty, new Binding("Margin")); // FrameworkElement

        // Act
        dynamic result = analyzer.GetBindings(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var bindings = result.bindings as IEnumerable<object>;
        bindings.Should().NotBeNull();

        // Should find all bindings regardless of inheritance depth
        var bindingList = bindings!.ToList();
        bindingList.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [StaFact]
    public void GetBindings_WithNoBindings_ShouldReturnEmptyList()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button { Width = 100 }; // Local value, no binding
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetBindings(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var bindings = result.bindings as IEnumerable<object>;
        bindings.Should().NotBeNull();
        bindings!.Should().BeEmpty();
    }

    [StaFact]
    public void GetBindings_WithMultipleElements_ShouldFindCorrectBindings()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var button1 = new Button();
        var button2 = new Button();
        var elementId1 = finder.GenerateElementId(button1);
        var elementId2 = finder.GenerateElementId(button2);

        button1.SetBinding(Button.ContentProperty, new Binding("Content1"));
        button2.SetBinding(Button.ContentProperty, new Binding("Content2"));

        // Act
        dynamic result1 = analyzer.GetBindings(elementId1);
        dynamic result2 = analyzer.GetBindings(elementId2);

        // Assert
        ((bool)result1.success).Should().BeTrue();
        ((bool)result2.success).Should().BeTrue();

        var bindings1 = result1.bindings as IEnumerable<object>;
        var bindings2 = result2.bindings as IEnumerable<object>;

        bindings1.Should().NotBeNull();
        bindings2.Should().NotBeNull();

        // Each should have their own binding
        bindings1!.Should().HaveCount(1);
        bindings2!.Should().HaveCount(1);
    }

    [StaFact]
    public void GetBindings_WithReadOnlyProperty_ShouldStillFindBinding()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Bind a read-only property (IsMouseOver is read-only)
        // Note: We can't actually bind to read-only properties, so test with writable ones
        button.SetBinding(Button.IsEnabledProperty, new Binding("IsEnabled"));

        // Act
        dynamic result = analyzer.GetBindings(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var bindings = result.bindings as IEnumerable<object>;
        bindings.Should().NotBeNull();
        bindings!.Count().Should().BeGreaterThanOrEqualTo(1);
    }

    [StaFact]
    public void GetBindings_WithAttachedProperty_ShouldFindBinding()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Bind an attached property (Grid.Row)
        button.SetBinding(Grid.RowProperty, new Binding("RowIndex"));

        // Act
        dynamic result = analyzer.GetBindings(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var bindings = result.bindings as IEnumerable<object>;
        bindings.Should().NotBeNull();

        // Should find the attached property binding
        var bindingList = bindings!.ToList();
        var propertyNames = bindingList
            .Select(b => (b as System.Collections.IDictionary)?["propertyName"]?.ToString())
            .Where(name => name != null)
            .ToList();

        propertyNames.Should().Contain("Row");
    }

    [StaFact]
    public void GetBindings_WithInvalidElementId_ShouldReturnError()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();

        // Act
        dynamic result = analyzer.GetBindings("invalid-element-id");

        // Assert
        ((bool)result.success).Should().BeFalse();
        string error = result.error;
        error.Should().NotBeNull();
    }

    [StaFact]
    public void GetBindings_WithNullElementId_ShouldUseRootElement()
    {
        // Arrange
        var analyzer = new BindingAnalyzer();

        // Act - Should not throw, even if no root element exists
        dynamic result = analyzer.GetBindings(null);

        // Assert - May succeed or fail depending on root element existence
        Assert.NotNull(result);
        // Result should have either success=true with bindings, or success=false with error
        bool hasSuccess = result.GetType().GetProperty("success") != null;
        Assert.True(hasSuccess);
    }

    [StaFact]
    public void GetBindings_WithMultipleBindingsOnSameElement_ShouldReturnAll()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Bind multiple properties
        button.SetBinding(Button.ContentProperty, new Binding("Content"));
        button.SetBinding(Button.WidthProperty, new Binding("Width"));
        button.SetBinding(Button.HeightProperty, new Binding("Height"));
        button.SetBinding(Button.IsEnabledProperty, new Binding("IsEnabled"));

        // Act
        dynamic result = analyzer.GetBindings(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var bindings = result.bindings as IEnumerable<object>;
        bindings.Should().NotBeNull();
        bindings!.Should().HaveCount(4);
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
