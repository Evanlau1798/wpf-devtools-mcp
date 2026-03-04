using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class MvvmAnalyzerTests
{

    [Fact]
    public void GetViewModel_WithNullElementId_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);

        // Act
        var result = analyzer.GetViewModel(null);

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void ModifyViewModel_WithValidProperty_ShouldModifyValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);

        var viewModel = new TestViewModel { Name = "Original" };
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ModifyViewModel(elementId, "Name", "Modified");

        // Assert
        result.Should().NotBeNull();
        viewModel.Name.Should().Be("Modified");
    }

    [StaFact]
    public void GetViewModel_WithNullDataContext_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var button = new Button { DataContext = null };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetViewModel(elementId);

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("no DataContext");
    }

    [StaFact]
    public void GetViewModel_WithPrimitiveDataContext_ShouldReturnProperties()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var button = new Button { DataContext = "TestString" };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetViewModel(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((string)result.viewModelType).Should().Be("String");
    }

    [StaFact]
    public void GetViewModel_WithCollectionDataContext_ShouldReturnProperties()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var list = new List<string> { "Item1", "Item2" };
        var button = new Button { DataContext = list };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetViewModel(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((IEnumerable<object>)result.properties).Should().NotBeEmpty();
    }

    [StaFact]
    public void GetViewModel_WithNonFrameworkElement_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var visual = new System.Windows.Media.DrawingVisual();
        var elementId = finder.GenerateElementId(visual);

        // Act
        dynamic result = analyzer.GetViewModel(elementId);

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("not a FrameworkElement");
    }

    [StaFact]
    public void GetCommands_WithNoCommands_ShouldReturnEmptyArray()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new TestViewModel { Name = "Test" };
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetCommands(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((IEnumerable<object>)result.commands).Should().BeEmpty();
    }

    [StaFact]
    public void GetCommands_WithMultipleCommands_ShouldReturnAll()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new ViewModelWithCommands();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetCommands(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((IEnumerable<object>)result.commands).Should().HaveCount(2);
    }

    [StaFact]
    public void GetCommands_WithCanExecuteFalse_ShouldReportCorrectly()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new ViewModelWithCommands { CanExecuteSave = false };
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.GetCommands(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        var commands = (IEnumerable<object>)result.commands;
        var saveCommand = commands
            .Cast<System.Collections.IDictionary>()
            .FirstOrDefault(c => c["name"]?.ToString() == "SaveCommand");
        saveCommand.Should().NotBeNull();
        ((bool)saveCommand!["canExecute"]!).Should().BeFalse();
    }

    [StaFact]
    public void ExecuteCommand_WithValidCommand_ShouldExecute()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new ViewModelWithCommands();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ExecuteCommand(elementId, "SaveCommand", null);

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((bool)result.executed).Should().BeTrue();
        viewModel.SaveExecuted.Should().BeTrue();
    }

    [StaFact]
    public void ExecuteCommand_WithCanExecuteFalse_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new ViewModelWithCommands { CanExecuteSave = false };
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ExecuteCommand(elementId, "SaveCommand", null);

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("cannot execute");
    }

    [StaFact]
    public void ExecuteCommand_WithNonExistentCommand_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new TestViewModel();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ExecuteCommand(elementId, "NonExistentCommand", null);

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("not found");
    }

    [StaFact]
    public void ExecuteCommand_WithNonCommandProperty_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new TestViewModel { Name = "Test" };
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ExecuteCommand(elementId, "Name", null);

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("not an ICommand");
    }

    [StaFact]
    public void GetValidationErrors_WithNoErrors_ShouldReturnEmptyList()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        // Act
        dynamic result = analyzer.GetValidationErrors(elementId);

        // Assert
        ((bool)result.success).Should().BeTrue();
        ((int)result.errorCount).Should().Be(0);
    }

    [StaFact]
    public void GetValidationErrors_WithInvalidElement_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);

        // Act
        dynamic result = analyzer.GetValidationErrors("InvalidId");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("not found");
    }

    [StaFact]
    public void ModifyViewModel_WithNullPropertyName_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new TestViewModel();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ModifyViewModel(elementId, null!, "value");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("propertyName is required");
    }

    [StaFact]
    public void ModifyViewModel_WithNonExistentProperty_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new TestViewModel();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ModifyViewModel(elementId, "NonExistent", "value");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("not found");
    }

    [StaFact]
    public void ModifyViewModel_WithReadOnlyProperty_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new ViewModelWithReadOnlyProperty();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ModifyViewModel(elementId, "ReadOnlyProperty", "value");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("read-only");
    }

    [StaFact]
    public void ModifyViewModel_WithTypeConversion_ShouldConvertAndSet()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new ViewModelWithTypes();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ModifyViewModel(elementId, "Age", "25");

        // Assert
        ((bool)result.success).Should().BeTrue();
        viewModel.Age.Should().Be(25);
    }

    [StaFact]
    public void ModifyViewModel_WithNullValue_ShouldSetNull()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new TestViewModel { Name = "Original" };
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ModifyViewModel(elementId, "Name", null!);

        // Assert
        ((bool)result.success).Should().BeTrue();
        viewModel.Name.Should().BeNull();
    }

    [StaFact]
    public void ModifyViewModel_WithInvalidTypeConversion_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new ViewModelWithTypes();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        dynamic result = analyzer.ModifyViewModel(elementId, "Age", "not_a_number");

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Contain("conversion failed");
    }

    private class TestViewModel
    {
        public string? Name { get; set; } = string.Empty;
    }

    private class ViewModelWithCommands
    {
        public bool CanExecuteSave { get; set; } = true;
        public bool SaveExecuted { get; set; }
        public bool DeleteExecuted { get; set; }

        public System.Windows.Input.ICommand SaveCommand => new RelayCommand(
            _ => SaveExecuted = true,
            _ => CanExecuteSave);

        public System.Windows.Input.ICommand DeleteCommand => new RelayCommand(
            _ => DeleteExecuted = true,
            _ => true);
    }

    private class ViewModelWithReadOnlyProperty
    {
        public string ReadOnlyProperty => "ReadOnly";
    }

    private class ViewModelWithTypes
    {
        public int Age { get; set; }
        public double Price { get; set; }
        public bool IsActive { get; set; }
    }

    private class RelayCommand : System.Windows.Input.ICommand
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
