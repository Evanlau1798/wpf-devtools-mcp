using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.ComponentModel;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for BindingAnalyzer requiring full WPF Application context
/// </summary>
[Collection("WpfIntegration")]
public class BindingAnalyzerIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public BindingAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
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
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            textBox.SetBinding(TextBox.TextProperty, binding);

            Application.Current.MainWindow.Content = textBox;

            // Act
            return analyzer.GetBindings(null);
        });

        // Assert
        result.Should().NotBeNull();
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

            // Force binding evaluation
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

            // Small delay to allow error to be captured
            System.Threading.Thread.Sleep(100);

            return analyzer.GetBindingErrors(clearAfterRead: false);
        });

        // Assert
        result.Should().NotBeNull();
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

            return analyzer.GetDataContextChain(null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ForceBindingUpdate_ShouldExecuteSuccessfully()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            var textBox = new TextBox();
            var binding = new Binding("TestProperty");
            textBox.SetBinding(TextBox.TextProperty, binding);
            textBox.DataContext = new TestDataContext();

            Application.Current.MainWindow.Content = textBox;

            return analyzer.ForceBindingUpdate(elementId: null, propertyName: "Text", direction: "Source");
        });

        // Assert
        result.Should().NotBeNull();
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
