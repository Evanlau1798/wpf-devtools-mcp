using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.ComponentModel;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for BindingAnalyzer requiring full WPF Application context
/// </summary>
[Collection("WpfAndBootstrapIntegration")]
public sealed class BindingAnalyzerIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;

    public BindingAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        BindingErrorTraceListener.ResetInstance();
    }

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    [Fact]
    public void GetBindings_WithValidBindings_ShouldReturnBindingInfo()
    {
        // Arrange
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            var textBox = new TextBox();
            var binding = new Binding("TestProperty")
            {
                Source = new { TestProperty = "Bound Value" },
                Mode = BindingMode.OneWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            textBox.SetBinding(TextBox.TextProperty, binding);

            Application.Current.MainWindow.Content = textBox;
            var elementId = elementFinder.GenerateElementId(textBox);

            // Act
            return JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId));
        });

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("bindings").GetArrayLength().Should().Be(1);
        result.GetProperty("bindings")[0].GetProperty("propertyName").GetString().Should().Be("Text");
        result.GetProperty("bindings")[0].GetProperty("path").GetString().Should().Be("TestProperty");
        result.GetProperty("bindings")[0].GetProperty("currentValue").GetString().Should().Be("Bound Value");
    }

    [Fact]
    public void GetBindingErrors_ShouldCaptureBindingErrors()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            // Create binding with invalid path to trigger error
            var textBox = new TextBox();
            var binding = new Binding("NonExistentProperty");
            textBox.SetBinding(TextBox.TextProperty, binding);
            textBox.DataContext = new { ValidProperty = "test" };

            Application.Current.MainWindow.Content = textBox;

            // Force binding evaluation (synchronous)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

            return JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: false));
        });

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("bindingPath").GetString())
            .Should().Contain("NonExistentProperty");
        result.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("propertyName").GetString())
            .Should().Contain("Text");
    }

    [Fact]
    public void GetDataContextChain_WithNestedElements_ShouldReturnChain()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            var stackPanel = new StackPanel
            {
                DataContext = new { Level1 = "Root" }
            };

            var border = new Border
            {
                DataContext = new { Level2 = "Middle" }
            };

            var textBox = new TextBox
            {
                DataContext = new { Level3 = "Leaf" }
            };

            border.Child = textBox;
            stackPanel.Children.Add(border);
            Application.Current.MainWindow.Content = stackPanel;

            var elementId = elementFinder.GenerateElementId(textBox);
            return JsonSerializer.SerializeToElement(analyzer.GetDataContextChain(elementId));
        });

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("chain").GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        result.GetProperty("chain")[0].GetProperty("elementType").GetString().Should().Be("TextBox");
        result.GetProperty("chain")[1].GetProperty("elementType").GetString().Should().Be("Border");
        result.GetProperty("chain")[2].GetProperty("elementType").GetString().Should().Be("StackPanel");
        result.GetProperty("chain")[0].GetProperty("hasDataContext").GetBoolean().Should().BeTrue();
        result.GetProperty("chain")[1].GetProperty("hasDataContext").GetBoolean().Should().BeTrue();
        result.GetProperty("chain")[2].GetProperty("hasDataContext").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ForceBindingUpdate_ShouldExecuteSuccessfully()
    {
        // Arrange & Act
        string? valueBeforeUpdate = null;
        string? valueAfterUpdate = null;
        string? targetValueBeforeUpdate = null;
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            var textBox = new TextBox();
            var binding = new Binding("TestProperty")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit
            };
            textBox.SetBinding(TextBox.TextProperty, binding);
            var viewModel = new TestDataContext();
            textBox.DataContext = viewModel;

            Application.Current.MainWindow.Content = textBox;
            Application.Current.MainWindow.UpdateLayout();
            var elementId = elementFinder.GenerateElementId(textBox);
            textBox.Text = "Updated";
            valueBeforeUpdate = viewModel.TestProperty;
            targetValueBeforeUpdate = textBox.Text;

            var updateResult = JsonSerializer.SerializeToElement(
                analyzer.ForceBindingUpdate(elementId, propertyName: "Text", direction: "Source"));
            valueAfterUpdate = viewModel.TestProperty;
            return updateResult;
        });

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("direction").GetString().Should().Be("Source");
        result.GetProperty("propertyName").GetString().Should().Be("Text");
        valueBeforeUpdate.Should().Be("Initial");
        targetValueBeforeUpdate.Should().Be("Updated");
        valueAfterUpdate.Should().Be("Updated");
    }

    private class TestDataContext : INotifyPropertyChanged
    {
        private string _testProperty = "Initial";

        public string TestProperty
        {
            get => _testProperty;
            set
            {
                _testProperty = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestProperty)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
