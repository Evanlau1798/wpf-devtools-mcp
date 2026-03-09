using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class MvvmAnalyzerCommandContractTests
{
    [StaFact]
    public void ExecuteCommand_ShouldReturnCanExecuteMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new CommandViewModel();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.ExecuteCommand(elementId, nameof(CommandViewModel.SaveCommand), null)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("commandName").GetString().Should().Be(nameof(CommandViewModel.SaveCommand));
        result.GetProperty("executed").GetBoolean().Should().BeTrue();
        result.GetProperty("canExecute").GetBoolean().Should().BeTrue();
    }

    private sealed class CommandViewModel
    {
        public bool Executed { get; private set; }

        public System.Windows.Input.ICommand SaveCommand => new RelayCommand(
            _ => Executed = true,
            _ => true);
    }

    private sealed class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool> _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);
    }
}
