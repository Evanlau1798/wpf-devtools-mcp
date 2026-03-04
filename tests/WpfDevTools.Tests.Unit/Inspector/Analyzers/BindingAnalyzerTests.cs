using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class BindingAnalyzerTests : IDisposable
{
    public BindingAnalyzerTests()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
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
        var analyzer = new BindingAnalyzer();

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
        var analyzer = new BindingAnalyzer();
        var listener = BindingErrorTraceListener.Instance;

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
        var analyzer = new BindingAnalyzer();
        var listener = BindingErrorTraceListener.Instance;
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
        var analyzer = new BindingAnalyzer();
        var listener = BindingErrorTraceListener.Instance;
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40, "Error 1");

        // Act
        dynamic result1 = analyzer.GetBindingErrors(clearAfterRead: false);
        dynamic result2 = analyzer.GetBindingErrors(clearAfterRead: false);

        // Assert - errors should persist across reads
        ((int)result1.errorCount).Should().Be(1);
        ((int)result2.errorCount).Should().Be(1);
    }

    private class TestViewModel
    {
        public string Value { get; set; } = string.Empty;
    }
}
