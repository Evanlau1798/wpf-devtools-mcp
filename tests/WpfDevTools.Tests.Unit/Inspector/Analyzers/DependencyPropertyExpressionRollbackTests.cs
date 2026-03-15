using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyExpressionRollbackTests
{
    [StaFact]
    public void ClearValue_ShouldRestoreCapturedBindingExpression_AfterSetValueReplacedIt()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var textBox = new TextBox();
        BindingOperations.SetBinding(textBox, TextBox.TextProperty, new Binding(nameof(SampleViewModel.Name))
        {
            Source = new SampleViewModel { Name = "Alice" },
            Mode = BindingMode.TwoWay
        });
        var elementId = finder.GenerateElementId(textBox);

        var setResult = JsonSerializer.SerializeToElement(analyzer.SetValue("Text", "Bob", elementId));
        var clearResult = JsonSerializer.SerializeToElement(analyzer.ClearValue("Text", elementId));
        var valueSourceResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Text", elementId));

        setResult.GetProperty("success").GetBoolean().Should().BeTrue();
        setResult.GetProperty("replacedExpression").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("restoredExpression").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("expressionKind").GetString().Should().Be("Binding");
        clearResult.GetProperty("newValue").GetString().Should().Be("Alice");
        valueSourceResult.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        valueSourceResult.GetProperty("currentValue").GetString().Should().Be("Alice");
    }

    [StaFact]
    public void ClearValue_ShouldPreserveDataContextBindingUpdates_AfterRestoringCapturedExpression()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var viewModel = new NotifyingViewModel { Name = "Alice" };
        var textBox = new TextBox
        {
            DataContext = viewModel
        };
        BindingOperations.SetBinding(textBox, TextBox.TextProperty, new Binding(nameof(NotifyingViewModel.Name))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        var elementId = finder.GenerateElementId(textBox);

        var setResult = JsonSerializer.SerializeToElement(analyzer.SetValue("Text", "Bob", elementId));
        var clearResult = JsonSerializer.SerializeToElement(analyzer.ClearValue("Text", elementId));
        viewModel.Name = "Carol";
        var valueSourceResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Text", elementId));

        setResult.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("restoredExpression").GetBoolean().Should().BeTrue();
        valueSourceResult.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        valueSourceResult.GetProperty("currentValue").GetString().Should().Be("Carol");
    }

    private sealed class SampleViewModel
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class NotifyingViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
