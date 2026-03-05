using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Input;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for MvvmAnalyzer requiring full WPF Application context
/// </summary>
[Collection("WpfIntegration")]
public class MvvmAnalyzerIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public MvvmAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetViewModel_WithDataContext_ShouldReturnViewModel()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetViewModel(elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetCommands_WithViewModel_ShouldReturnCommands()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetCommands(elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ExecuteCommand_WithValidCommand_ShouldExecute()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.ExecuteCommand(elementId: null, commandName: "TestCommand", parameter: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ModifyViewModel_ShouldUpdateProperty()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.ModifyViewModel(elementId: null, propertyName: "Name", value: "Modified");
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetValidationErrors_WithValidationErrors_ShouldReturnErrors()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "", Age = -1 }; // Invalid values
            var stackPanel = new StackPanel { DataContext = viewModel };

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetValidationErrors(elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    private class TestViewModel : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _name = "";
        private int _age;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public int Age
        {
            get => _age;
            set
            {
                _age = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age)));
            }
        }

        public ICommand TestCommand { get; }

        public TestViewModel()
        {
            TestCommand = new RelayCommand(_ => { }, _ => true);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                return columnName switch
                {
                    nameof(Name) when string.IsNullOrWhiteSpace(Name) => "Name is required",
                    nameof(Age) when Age < 0 => "Age must be positive",
                    _ => string.Empty
                };
            }
        }
    }

    private class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool> _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
#pragma warning disable CS0067 // ICommand interface requirement
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }
}
