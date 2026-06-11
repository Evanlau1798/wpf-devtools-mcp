using System.ComponentModel;
using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Data;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class MvvmAnalyzerValidationEventTests
{
    [StaFact]
    public void ModifyViewModel_WhenValidationStateTransitions_ShouldEnqueueValidationChangeEvent()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 16, new WatchEventDeduplicator());
        var analyzer = new MvvmAnalyzer(finder, buffer);
        var viewModel = new ValidatingViewModel { Name = "Alice" };
        var textBox = new TextBox
        {
            DataContext = viewModel
        };
        var elementId = finder.GenerateElementId(textBox);

        textBox.SetBinding(TextBox.TextProperty, new Binding(nameof(ValidatingViewModel.Name))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            ValidatesOnDataErrors = true
        });
        BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)!.UpdateTarget();

        analyzer.ModifyViewModel(elementId, nameof(ValidatingViewModel.Name), string.Empty);

        buffer.GetSnapshot().Should().Contain(record =>
            record.EventType == "ValidationChange"
            && record.ElementId == elementId
            && record.NewValue == "0->1");
    }

    private sealed class ValidatingViewModel : INotifyPropertyChanged, IDataErrorInfo
    {
        private string? _name;

        public string? Name
        {
            get => _name;
            set
            {
                if (string.Equals(_name, value, StringComparison.Ordinal))
                {
                    return;
                }

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public string Error => string.Empty;

        public string this[string columnName] =>
            string.Equals(columnName, nameof(Name), StringComparison.Ordinal) && string.IsNullOrWhiteSpace(Name)
                ? "Name is required"
                : string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
