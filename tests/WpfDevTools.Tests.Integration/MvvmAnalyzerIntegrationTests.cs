using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Data;

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
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };
            var stackPanelId = elementFinder.GenerateElementId(stackPanel);

            Application.Current.MainWindow.Content = stackPanel;
            EvictElementCacheEntry(elementFinder, stackPanelId);

            return analyzer.GetViewModel(stackPanelId);
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("viewModelType").GetString().Should().Be("TestViewModel");
        json.GetProperty("implementsINotifyPropertyChanged").GetBoolean().Should().BeTrue();
        json.GetProperty("properties").EnumerateArray().Should().ContainSingle(p =>
            p.GetProperty("name").GetString() == "Name"
            && p.GetProperty("value").GetString() == "Test"
            && p.GetProperty("canWrite").GetBoolean());
        json.GetProperty("properties").EnumerateArray().Should().ContainSingle(p =>
            p.GetProperty("name").GetString() == "Age"
            && p.GetProperty("value").GetString() == "25");
    }

    [Fact]
    public void GetCommands_WithViewModel_ShouldReturnCommands()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };
            var stackPanelId = elementFinder.GenerateElementId(stackPanel);

            Application.Current.MainWindow.Content = stackPanel;
            EvictElementCacheEntry(elementFinder, stackPanelId);

            return analyzer.GetCommands(stackPanelId);
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("commands").EnumerateArray().Should().ContainSingle(command =>
            command.GetProperty("name").GetString() == "TestCommand"
            && command.GetProperty("type").GetString() == "ICommand"
            && command.GetProperty("canExecute").GetBoolean());
    }

    [Fact]
    public void ExecuteCommand_WithValidCommand_ShouldExecute()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };
            var stackPanelId = elementFinder.GenerateElementId(stackPanel);

            Application.Current.MainWindow.Content = stackPanel;
            EvictElementCacheEntry(elementFinder, stackPanelId);

            var commandResult = analyzer.ExecuteCommand(stackPanelId, commandName: "TestCommand", parameter: null);
            return new
            {
                result = commandResult,
                executionCount = viewModel.CommandExecutions
            };
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("executionCount").GetInt32().Should().Be(1);
        json.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("result").GetProperty("commandName").GetString().Should().Be("TestCommand");
        json.GetProperty("result").GetProperty("executed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ModifyViewModel_ShouldUpdateProperty()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };
            var stackPanelId = elementFinder.GenerateElementId(stackPanel);

            Application.Current.MainWindow.Content = stackPanel;
            EvictElementCacheEntry(elementFinder, stackPanelId);

            var modifyResult = analyzer.ModifyViewModel(stackPanelId, propertyName: "Name", value: "Modified");
            return new
            {
                result = modifyResult,
                actualName = viewModel.Name
            };
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("actualName").GetString().Should().Be("Modified");
        json.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("result").GetProperty("propertyName").GetString().Should().Be("Name");
        json.GetProperty("result").GetProperty("oldValue").GetString().Should().Be("Test");
        json.GetProperty("result").GetProperty("newValue").GetString().Should().Be("Modified");
    }

    [Fact]
    public void GetValidationErrors_WithValidationErrors_ShouldReturnErrors()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "", Age = -1 }; // Invalid values
            var nameTextBox = new TextBox();
            nameTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(TestViewModel.Name))
            {
                ValidatesOnDataErrors = true,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            var ageTextBox = new TextBox();
            ageTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(TestViewModel.Age))
            {
                ValidatesOnDataErrors = true,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            var stackPanel = new StackPanel { DataContext = viewModel };
            stackPanel.Children.Add(nameTextBox);
            stackPanel.Children.Add(ageTextBox);
            var stackPanelId = elementFinder.GenerateElementId(stackPanel);

            Application.Current.MainWindow.Content = stackPanel;
            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.UpdateLayout();
            nameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            ageTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            EvictElementCacheEntry(elementFinder, stackPanelId);

            return analyzer.GetValidationErrors(stackPanelId);
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("errorCount").GetInt32().Should().Be(2);
        json.GetProperty("errors").EnumerateArray().Should().Contain(error =>
            error.GetProperty("errorContent").GetString() == "Name is required");
        json.GetProperty("errors").EnumerateArray().Should().Contain(error =>
            error.GetProperty("errorContent").GetString() == "Age must be positive");
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private class TestViewModel : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _name = "";
        private int _age;

        public int CommandExecutions { get; private set; }

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
            TestCommand = new RelayCommand(_ => CommandExecutions++, _ => true);
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
