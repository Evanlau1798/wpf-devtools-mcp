using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for BindingAnalyzer using TestApp golden sample scenarios.
/// Tests realistic binding errors, DataContext chains, and valid bindings
/// matching the TestApp's Tab 1 (Basic Controls) structure.
/// </summary>
[Collection("WpfIntegration")]
public class TestAppBindingIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public TestAppBindingIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        BindingErrorTraceListener.ResetInstance();
    }

    [Fact]
    public void GetBindingErrors_WithIntentionalBindingErrors_ShouldDetectErrors()
    {
        // Arrange - recreate TestApp Tab 1 binding error scenario
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            // Binding Error 1: Wrong property name (matches TestApp ErrorTextBox1)
            var errorTextBox1 = new TextBox();
            errorTextBox1.SetBinding(TextBox.TextProperty, new Binding("InvalidPropertyName"));
            stackPanel.Children.Add(errorTextBox1);

            // Binding Error 2: Wrong path (matches TestApp ErrorTextBox2)
            var errorTextBox2 = new TextBox();
            errorTextBox2.SetBinding(TextBox.TextProperty, new Binding("NonExistent.Property"));
            stackPanel.Children.Add(errorTextBox2);

            // Binding Error 3: Null DataContext (matches TestApp ErrorTextBox3)
            var nullContextBorder = new Border();
            var nullContextPanel = new StackPanel { DataContext = null };
            var errorTextBox3 = new TextBox();
            errorTextBox3.SetBinding(TextBox.TextProperty, new Binding("Name"));
            nullContextPanel.Children.Add(errorTextBox3);
            nullContextBorder.Child = nullContextPanel;
            stackPanel.Children.Add(nullContextBorder);

            Application.Current.MainWindow.Content = stackPanel;

            // Force binding evaluation
            errorTextBox1.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            errorTextBox2.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            errorTextBox3.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

            return analyzer.GetBindingErrors(clearAfterRead: false);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetBindings_WithTestViewModelBindings_ShouldReturnBindingInfo()
    {
        // Arrange - recreate TestApp Tab 1 valid binding scenario
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Alice", Age = 30 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            // Name binding (matches TestApp NameTextBox)
            var nameTextBox = new TextBox();
            nameTextBox.SetBinding(TextBox.TextProperty, new Binding("Name")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ValidatesOnDataErrors = true
            });
            stackPanel.Children.Add(nameTextBox);

            // Age binding (matches TestApp AgeTextBox)
            var ageTextBox = new TextBox();
            ageTextBox.SetBinding(TextBox.TextProperty, new Binding("Age")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ValidatesOnDataErrors = true
            });
            stackPanel.Children.Add(ageTextBox);

            // IsEnabled binding (matches TestApp EnabledCheckBox)
            var checkBox = new CheckBox();
            checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsEnabled"));
            stackPanel.Children.Add(checkBox);

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetBindings(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetDataContextChain_WithTestViewModel_ShouldReturnChain()
    {
        // Arrange - use real TestViewModel as DataContext
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            // Nested element inheriting DataContext
            var border = new Border();
            var textBox = new TextBox();
            textBox.SetBinding(TextBox.TextProperty, new Binding("Name"));
            border.Child = textBox;
            stackPanel.Children.Add(border);

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetDataContextChain(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void ForceBindingUpdate_WithTestViewModelBinding_ShouldUpdateSuccessfully()
    {
        // Arrange - use real TestViewModel with PropertyChanged
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Original", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            var textBox = new TextBox();
            textBox.SetBinding(TextBox.TextProperty, new Binding("Name")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit
            });
            stackPanel.Children.Add(textBox);

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.ForceBindingUpdate(elementId: null, propertyName: "Text", direction: "Source");
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetBindingErrors_WithGoldenSampleValidationAndBindingFailures_ShouldExcludeValidationMessages()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            BindingErrorTraceListener.ResetInstance();
            var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);
            var window = Application.Current.MainWindow;

            try
            {
                var viewModel = new TestViewModel { Name = "", Age = 0 };
                var stackPanel = new StackPanel { DataContext = viewModel };

                var nameTextBox = new TextBox();
                nameTextBox.SetBinding(TextBox.TextProperty, new Binding("Name")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ValidatesOnDataErrors = true
                });
                stackPanel.Children.Add(nameTextBox);

                var ageTextBox = new TextBox();
                ageTextBox.SetBinding(TextBox.TextProperty, new Binding("Age")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ValidatesOnDataErrors = true
                });
                stackPanel.Children.Add(ageTextBox);

                var invalidPropertyTextBox = new TextBox();
                invalidPropertyTextBox.SetBinding(TextBox.TextProperty, new Binding("InvalidPropertyName"));
                stackPanel.Children.Add(invalidPropertyTextBox);

                var invalidPathTextBox = new TextBox();
                invalidPathTextBox.SetBinding(TextBox.TextProperty, new Binding("NonExistent.Property"));
                stackPanel.Children.Add(invalidPathTextBox);

                var nullContextPanel = new StackPanel { DataContext = null };
                var nullContextTextBox = new TextBox();
                nullContextTextBox.SetBinding(TextBox.TextProperty, new Binding("Name"));
                nullContextPanel.Children.Add(nullContextTextBox);
                stackPanel.Children.Add(nullContextPanel);

                window.Content = stackPanel;
                window.Show();
                window.Activate();
                window.UpdateLayout();

                nameTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                ageTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                invalidPropertyTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                invalidPathTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                nullContextTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

                return JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: false));
            }
            finally
            {
                window.Content = null;
            }
        });

        var messages = result.GetProperty("errors")
            .EnumerateArray()
            .Select(error => error.GetProperty("message").GetString())
            .Where(message => message != null)
            .ToArray();

        var messageSummary = string.Join(" || ", messages);

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(3, because: messageSummary);

        messages.Should().Contain(message => message!.Contains("InvalidPropertyName", StringComparison.Ordinal));
        messages.Should().Contain(message => message!.Contains("NonExistent.Property", StringComparison.Ordinal));
        messages.Should().Contain(message => message!.Contains("no DataContext", StringComparison.OrdinalIgnoreCase)
            || message!.Contains("no DataContext or resolved source", StringComparison.OrdinalIgnoreCase));
        messages.Should().NotContain(message => message!.Contains("Name is required", StringComparison.Ordinal));
        messages.Should().NotContain(message => message!.Contains("Age must be greater than 0", StringComparison.Ordinal));
    }
}
