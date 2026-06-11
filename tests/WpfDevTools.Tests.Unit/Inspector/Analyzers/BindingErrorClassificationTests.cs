using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingErrorClassificationTests
{
    private static (BindingAnalyzer Analyzer, BindingErrorTraceListener Listener) CreateBindingErrorAnalyzer(
        ElementFinder? finder = null)
    {
        var actualFinder = finder ?? new ElementFinder();
        var listener = BindingErrorTraceListener.CreateForTesting();
        return (new BindingAnalyzer(actualFinder, null, listener), listener);
    }

    [StaFact]
    public void GetBindingErrors_WhenOnlyValidationErrorsExist_ShouldReturnNoBindingErrors()
    {
        var finder = new ElementFinder();
        var (analyzer, _) = CreateBindingErrorAnalyzer(finder);
        using var hostScope = WindowHostScope.Create();
        var hostWindow = hostScope.Window;
        var root = new StackPanel();
        var nameTextBox = new TextBox();

        nameTextBox.SetBinding(TextBox.TextProperty, new Binding("Name")
        {
            Source = new TestViewModel { Name = "Alice", Age = 20 },
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

        root.Children.Add(nameTextBox);
        hostWindow.Content = root;
        hostWindow.Show();
        hostWindow.UpdateLayout();
        var expression = nameTextBox.GetBindingExpression(TextBox.TextProperty)!;
        Validation.MarkInvalid(
            expression,
            new ValidationError(new ExceptionValidationRule(), expression)
            {
                ErrorContent = "Validation-only failure"
            });

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void GetBindingErrors_WhenTraceMessageMatchesActiveValidationError_ShouldFilterItOut()
    {
        var finder = new ElementFinder();
        var (analyzer, listener) = CreateBindingErrorAnalyzer(finder);
        using var hostScope = WindowHostScope.Create();
        var hostWindow = hostScope.Window;
        var root = new StackPanel();
        var nameTextBox = new TextBox();

        nameTextBox.SetBinding(TextBox.TextProperty, new Binding("Name")
        {
            Source = new TestViewModel { Name = string.Empty, Age = 20 },
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            ValidatesOnDataErrors = true
        });

        root.Children.Add(nameTextBox);
        hostWindow.Content = root;
        hostWindow.Show();
        hostWindow.UpdateLayout();
        nameTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        listener.TraceEvent(
            null,
            "System.Windows.Data",
            TraceEventType.Error,
            7,
            "System.Windows.Data Error: validation failed: Name is required");

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
    }
}
