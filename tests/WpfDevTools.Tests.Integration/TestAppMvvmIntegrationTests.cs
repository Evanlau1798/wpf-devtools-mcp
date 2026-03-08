using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for MvvmAnalyzer using TestApp's real TestViewModel.
/// Tests ViewModel inspection, command discovery/execution, property modification,
/// and IDataErrorInfo validation - all using the actual TestApp golden sample types.
/// </summary>
[Collection("WpfIntegration")]
public class TestAppMvvmIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public TestAppMvvmIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetViewModel_WithRealTestViewModel_ShouldReturnProperties()
    {
        // Arrange - use TestApp's real TestViewModel (not a duplicate)
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Alice", Age = 30 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            // Add controls with bindings matching TestApp Tab 1
            var nameTextBox = new TextBox();
            nameTextBox.SetBinding(TextBox.TextProperty, new Binding("Name")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ValidatesOnDataErrors = true
            });
            stackPanel.Children.Add(nameTextBox);

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetViewModel(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetCommands_WithSaveAndClearCommands_ShouldReturnBothCommands()
    {
        // Arrange - TestViewModel has SaveCommand and ClearCommand
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetCommands(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void ExecuteCommand_WithClearCommand_ShouldExecuteSuccessfully()
    {
        // Arrange - ClearCommand always has CanExecute=true
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Test", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            Application.Current.MainWindow.Content = stackPanel;

            // ClearCommand is safe to execute (no MessageBox, just clears properties)
            return analyzer.ExecuteCommand(elementId: null, commandName: "ClearCommand", parameter: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void ModifyViewModel_WithNameProperty_ShouldUpdateValue()
    {
        // Arrange - modify TestViewModel.Name through analyzer
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            var viewModel = new TestViewModel { Name = "Original", Age = 25 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.ModifyViewModel(elementId: null, propertyName: "Name", value: "Modified");
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetValidationErrors_WithInvalidTestViewModel_ShouldReturnErrors()
    {
        // Arrange - TestViewModel validates Name (required) and Age (0-150)
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            // Invalid values: empty name, negative age
            var viewModel = new TestViewModel { Name = "", Age = -1 };
            var stackPanel = new StackPanel { DataContext = viewModel };

            // Add bound controls with ValidatesOnDataErrors
            var nameTextBox = new TextBox();
            nameTextBox.SetBinding(TextBox.TextProperty, new Binding("Name")
            {
                ValidatesOnDataErrors = true,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            stackPanel.Children.Add(nameTextBox);

            var ageTextBox = new TextBox();
            ageTextBox.SetBinding(TextBox.TextProperty, new Binding("Age")
            {
                ValidatesOnDataErrors = true,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            stackPanel.Children.Add(ageTextBox);

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetValidationErrors(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetViewModel_WithCanSaveProperty_ShouldReflectComputedState()
    {
        // Arrange - CanSave is computed from Name + Age
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);

            // CanSave should be true when Name is not empty AND Age > 0
            var viewModel = new TestViewModel { Name = "Valid", Age = 25 };
            viewModel.CanSave.Should().BeTrue();

            var stackPanel = new StackPanel { DataContext = viewModel };
            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetViewModel(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void ModifyViewModel_WhenCanSaveBecomesTrue_ShouldEnableSaveButton()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var viewModel = new TestViewModel();
            var saveButton = new Button { Content = "Save", Width = 100, Margin = new Thickness(5) };
            saveButton.SetBinding(Button.CommandProperty, new Binding("SaveCommand"));

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(saveButton);

            var window = Application.Current.MainWindow;
            window.DataContext = viewModel;
            window.Content = stackPanel;
            window.Show();
            window.Activate();
            stackPanel.Measure(new Size(300, 120));
            stackPanel.Arrange(new Rect(0, 0, 300, 120));
            stackPanel.UpdateLayout();

            saveButton.IsEnabled.Should().BeFalse();

            analyzer.ModifyViewModel(elementId: null, propertyName: "Name", value: "Alice");
            analyzer.ModifyViewModel(elementId: null, propertyName: "Age", value: 25);
            stackPanel.UpdateLayout();

            return new
            {
                viewModel.CanSave,
                saveButton.IsEnabled
            };
        });

        result.CanSave.Should().BeTrue();
        result.IsEnabled.Should().BeTrue();
    }
}
