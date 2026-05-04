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
/// Integration tests for MvvmAnalyzer using TestApp's real TestViewModel.
/// Tests ViewModel inspection, command discovery/execution, property modification,
/// and IDataErrorInfo validation - all using the actual TestApp golden sample types.
/// </summary>
[Collection("WpfAndBootstrapIntegration")]
public class TestAppMvvmIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private MainWindow? _activeTestAppWindow;

    public TestAppMvvmIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        _fixture.RunOnUIThread(() =>
        {
            if (Application.Current?.MainWindow is not { } mainWindow)
            {
                return;
            }

            if (_activeTestAppWindow != null)
            {
                _activeTestAppWindow.Close();
                _activeTestAppWindow = null;
                Application.Current.MainWindow = _previousMainWindow;
                _previousMainWindow = null;
                return;
            }

            mainWindow.DataContext = null;
            mainWindow.Content = null;
            mainWindow.Hide();
        });
    }

    [Fact]
    public void GetViewModel_WithRealTestViewModel_ShouldReturnProperties()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();

            context.ViewModel.Name = "Alice";
            context.ViewModel.Age = 30;
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.MainTabControl.SelectedItem = context.BasicControlsTab;
            context.Window.UpdateLayout();

            var panelId = elementFinder.GenerateElementId(context.BasicControlsStackPanel);
            EvictElementCacheEntry(elementFinder, panelId);

            return JsonSerializer.SerializeToElement(analyzer.GetViewModel(panelId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("viewModelType").GetString().Should().Be("TestViewModel");
        result.GetProperty("properties").EnumerateArray().Should().ContainSingle(property =>
            property.GetProperty("name").GetString() == "Name"
            && property.GetProperty("value").GetString() == "Alice");
        result.GetProperty("properties").EnumerateArray().Should().ContainSingle(property =>
            property.GetProperty("name").GetString() == "Age"
            && property.GetProperty("value").GetString() == "30");
        result.GetProperty("properties").EnumerateArray().Should().ContainSingle(property =>
            property.GetProperty("name").GetString() == "CanSave"
            && property.GetProperty("value").GetString() == "True");
    }

    [Fact]
    public void GetCommands_WithSaveAndClearCommands_ShouldReturnBothCommands()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();

            context.ViewModel.Name = "Test";
            context.ViewModel.Age = 25;
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.Window.UpdateLayout();
            context.SaveButton.Command.Should().BeSameAs(context.ViewModel.SaveCommand);
            context.ClearButton.Command.Should().BeSameAs(context.ViewModel.ClearCommand);

            var panelId = elementFinder.GenerateElementId(context.BasicControlsStackPanel);
            EvictElementCacheEntry(elementFinder, panelId);

            return JsonSerializer.SerializeToElement(analyzer.GetCommands(panelId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("commands").EnumerateArray().Should().ContainSingle(command =>
            command.GetProperty("name").GetString() == "SaveCommand"
            && command.GetProperty("canExecute").GetBoolean());
        result.GetProperty("commands").EnumerateArray().Should().ContainSingle(command =>
            command.GetProperty("name").GetString() == "ClearCommand"
            && command.GetProperty("canExecute").GetBoolean());
        result.GetProperty("commands").EnumerateArray().Should().ContainSingle(command =>
            command.GetProperty("name").GetString() == "ResetStateCommand"
            && command.GetProperty("canExecute").GetBoolean());
    }

    [Fact]
    public void ExecuteCommand_WithClearCommand_ShouldExecuteSuccessfully()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();

            context.ViewModel.Name = "Test";
            context.ViewModel.Age = 25;
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.Window.UpdateLayout();
            context.ClearButton.Command.Should().BeSameAs(context.ViewModel.ClearCommand);

            var clearButtonId = elementFinder.GenerateElementId(context.ClearButton);
            EvictElementCacheEntry(elementFinder, clearButtonId);

            var commandResult = analyzer.ExecuteCommand(clearButtonId, commandName: "ClearCommand", parameter: null);
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.Window.UpdateLayout();

            return JsonSerializer.SerializeToElement(new
            {
                result = commandResult,
                context.ViewModel.Name,
                context.ViewModel.Age,
                context.ViewModel.LastActionMessage
            });
        });

        result.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("commandName").GetString().Should().Be("ClearCommand");
        result.GetProperty("result").GetProperty("executed").GetBoolean().Should().BeTrue();
        result.GetProperty("Name").GetString().Should().BeEmpty();
        result.GetProperty("Age").GetInt32().Should().Be(0);
        result.GetProperty("LastActionMessage").GetString().Should().Be("Form cleared");
    }

    [Fact]
    public void ModifyViewModel_WithNameProperty_ShouldUpdateValue()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();

            context.ViewModel.Name = "Original";
            context.ViewModel.Age = 25;
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.Window.UpdateLayout();

            var nameTextBoxId = elementFinder.GenerateElementId(context.NameTextBox);
            EvictElementCacheEntry(elementFinder, nameTextBoxId);

            var modifyResult = analyzer.ModifyViewModel(nameTextBoxId, propertyName: "Name", value: "Modified");
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.Window.UpdateLayout();

            return JsonSerializer.SerializeToElement(new
            {
                result = modifyResult,
                context.ViewModel.Name,
                context.ViewModel.CanSave,
                nameText = context.NameTextBox.Text
            });
        });

        result.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("propertyName").GetString().Should().Be("Name");
        result.GetProperty("result").GetProperty("oldValue").GetString().Should().Be("Original");
        result.GetProperty("result").GetProperty("newValue").GetString().Should().Be("Modified");
        result.GetProperty("Name").GetString().Should().Be("Modified");
        result.GetProperty("CanSave").GetBoolean().Should().BeTrue();
        result.GetProperty("nameText").GetString().Should().Be("Modified");
    }

    [Fact]
    public void GetValidationErrors_WithInvalidTestViewModel_ShouldReturnErrors()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();

            context.ViewModel.Name = string.Empty;
            context.ViewModel.Age = -1;
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.MainTabControl.SelectedItem = context.BasicControlsTab;
            context.Window.UpdateLayout();

            var panelId = elementFinder.GenerateElementId(context.BasicControlsStackPanel);
            EvictElementCacheEntry(elementFinder, panelId);

            return JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(panelId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(2);
        result.GetProperty("errors").EnumerateArray().Should().Contain(error =>
            error.GetProperty("errorContent").GetString() == "Name is required");
        result.GetProperty("errors").EnumerateArray().Should().Contain(error =>
            error.GetProperty("errorContent").GetString() == "Age must be greater than 0");
    }

    [Fact]
    public void GetViewModel_WithCanSaveProperty_ShouldReflectComputedState()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();

            context.ViewModel.Name = "Valid";
            context.ViewModel.Age = 25;
            context.ViewModel.CanSave.Should().BeTrue();
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.Window.UpdateLayout();

            var panelId = elementFinder.GenerateElementId(context.BasicControlsStackPanel);
            EvictElementCacheEntry(elementFinder, panelId);

            return JsonSerializer.SerializeToElement(analyzer.GetViewModel(panelId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("properties").EnumerateArray().Should().ContainSingle(property =>
            property.GetProperty("name").GetString() == "CanSave"
            && property.GetProperty("type").GetString() == "Boolean"
            && property.GetProperty("value").GetString() == "True"
            && !property.GetProperty("canWrite").GetBoolean());
    }

    [Fact]
    public void GetValidationErrors_OnInactiveTabItem_ShouldAggregateRealTestViewModelErrors()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();

            context.ViewModel.Name = string.Empty;
            context.ViewModel.Age = 0;
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.MainTabControl.SelectedIndex = 1;
            context.Window.UpdateLayout();
            context.BasicControlsTab.IsSelected.Should().BeFalse();
            ReferenceEquals(context.MainTabControl.SelectedItem, context.BasicControlsTab).Should().BeFalse();

            var tabId = elementFinder.GenerateElementId(context.BasicControlsTab);
            EvictElementCacheEntry(elementFinder, tabId);
            return JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(tabId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(2);
        result.GetProperty("errors").EnumerateArray().Should().Contain(error =>
            error.GetProperty("errorContent").GetString() == "Name is required");
        result.GetProperty("errors").EnumerateArray().Should().Contain(error =>
            error.GetProperty("errorContent").GetString() == "Age must be greater than 0");
    }

    [Fact]
    public void ModifyViewModel_WhenCanSaveBecomesTrue_ShouldEnableSaveButton()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new MvvmAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            var saveButtonId = elementFinder.GenerateElementId(context.SaveButton);
            EvictElementCacheEntry(elementFinder, saveButtonId);

            context.ViewModel.ResetState();
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.Window.UpdateLayout();

            context.SaveButton.IsEnabled.Should().BeFalse();

            analyzer.ModifyViewModel(saveButtonId, propertyName: "Name", value: "Alice");
            analyzer.ModifyViewModel(saveButtonId, propertyName: "Age", value: 25);
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            context.Window.UpdateLayout();

            return new
            {
                context.ViewModel.CanSave,
                context.SaveButton.IsEnabled
            };
        });

        result.CanSave.Should().BeTrue();
        result.IsEnabled.Should().BeTrue();
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private TestAppWindowContext CreateRealTestAppWindow()
    {
        var application = Application.Current;
        application.Should().NotBeNull();

        _previousMainWindow ??= application!.MainWindow;

        var window = new MainWindow();
        _activeTestAppWindow = window;
        application.MainWindow = window;
        window.Show();
        window.UpdateLayout();

        var viewModel = window.DataContext as TestViewModel;
        var mainTabControl = window.FindName("MainTabControl") as TabControl;
        var basicControlsTab = window.FindName("BasicControlsTab") as TabItem;
        var basicControlsStackPanel = window.FindName("BasicControlsStackPanel") as StackPanel;
        var nameTextBox = window.FindName("NameTextBox") as TextBox;
        var ageTextBox = window.FindName("AgeTextBox") as TextBox;
        var saveButton = window.FindName("SaveButton") as Button;
        var clearButton = window.FindName("ClearButton") as Button;

        viewModel.Should().NotBeNull();
        mainTabControl.Should().NotBeNull();
        basicControlsTab.Should().NotBeNull();
        basicControlsStackPanel.Should().NotBeNull();
        nameTextBox.Should().NotBeNull();
        ageTextBox.Should().NotBeNull();
        saveButton.Should().NotBeNull();
        clearButton.Should().NotBeNull();

        return new TestAppWindowContext(
            window,
            viewModel!,
            mainTabControl!,
            basicControlsTab!,
            basicControlsStackPanel!,
            nameTextBox!,
            ageTextBox!,
            saveButton!,
            clearButton!);
    }

    private sealed record TestAppWindowContext(
        MainWindow Window,
        TestViewModel ViewModel,
        TabControl MainTabControl,
        TabItem BasicControlsTab,
        StackPanel BasicControlsStackPanel,
        TextBox NameTextBox,
        TextBox AgeTextBox,
        Button SaveButton,
        Button ClearButton);
}
