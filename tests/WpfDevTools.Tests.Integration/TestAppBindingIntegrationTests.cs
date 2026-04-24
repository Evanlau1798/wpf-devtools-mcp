using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for BindingAnalyzer using TestApp golden sample scenarios.
/// Tests realistic binding errors, DataContext chains, and valid bindings
/// matching the TestApp's Tab 1 (Basic Controls) structure.
/// </summary>
[Collection("WpfIntegration")]
public sealed class TestAppBindingIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private MainWindow? _activeTestAppWindow;

    public TestAppBindingIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        BindingErrorTraceListener.ResetInstance();
    }

    public void Dispose()
    {
        _fixture.RunOnUIThread(() =>
        {
            BindingErrorTraceListener.ResetInstance();

            if (_activeTestAppWindow == null)
            {
                return;
            }

            _activeTestAppWindow.Close();
            _activeTestAppWindow = null;

            if (Application.Current != null)
            {
                Application.Current.MainWindow = _previousMainWindow;
            }

            _previousMainWindow = null;
        });
    }

    [Fact]
    public void GetBindingErrors_WithIntentionalBindingErrors_ShouldDetectErrors()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            BindingErrorTraceListener.ResetInstance();
            using var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectBasicControlsTab(context);

            context.ViewModel.Name = "Alice";
            context.ViewModel.Age = 30;
            context.Window.UpdateLayout();

            context.ErrorTextBox1.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            context.ErrorTextBox2.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            context.ErrorTextBox3.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            context.BrushMismatchButton.GetBindingExpression(Button.BackgroundProperty)?.UpdateTarget();

            return JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: false));
        });

        var bindingPaths = result.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("bindingPath").GetString())
            .Where(path => path != null)
            .Cast<string>()
            .ToArray();
        var propertyNames = result.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("propertyName").GetString())
            .Where(name => name != null)
            .Cast<string>()
            .ToArray();

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(4);
        bindingPaths.Should().Contain("InvalidPropertyName");
        bindingPaths.Should().Contain("NonExistent.Property");
        bindingPaths.Should().Contain("Name");
        bindingPaths.Should().Contain("IsEnabled");
        propertyNames.Should().Contain("Text");
        propertyNames.Should().Contain("Background");
    }

    [Fact]
    public void GetBindings_WithTestViewModelBindings_ShouldReturnBindingInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectBasicControlsTab(context);

            context.ViewModel.Name = "Alice";
            context.ViewModel.Age = 30;
            context.ViewModel.IsEnabled = false;
            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            context.EnabledCheckBox.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();

            var nameTextBoxId = elementFinder.GenerateElementId(context.NameTextBox);
            var cachedNameResult = JsonSerializer.SerializeToElement(analyzer.GetBindings(nameTextBoxId));
            EvictElementCacheEntry(elementFinder, nameTextBoxId);
            var lookupNameResult = JsonSerializer.SerializeToElement(analyzer.GetBindings(nameTextBoxId));

            var ageTextBoxId = elementFinder.GenerateElementId(context.AgeTextBox);
            EvictElementCacheEntry(elementFinder, ageTextBoxId);
            var ageResult = JsonSerializer.SerializeToElement(analyzer.GetBindings(ageTextBoxId));

            var enabledCheckBoxId = elementFinder.GenerateElementId(context.EnabledCheckBox);
            EvictElementCacheEntry(elementFinder, enabledCheckBoxId);
            var enabledResult = JsonSerializer.SerializeToElement(analyzer.GetBindings(enabledCheckBoxId));

            return JsonSerializer.SerializeToElement(new
            {
                cachedNameResult,
                lookupNameResult,
                ageResult,
                enabledResult
            });
        });

        var cachedNameResult = result.GetProperty("cachedNameResult");
        var lookupNameResult = result.GetProperty("lookupNameResult");
        var ageResult = result.GetProperty("ageResult");
        var enabledResult = result.GetProperty("enabledResult");

        cachedNameResult.GetRawText().Should().Be(lookupNameResult.GetRawText());
        lookupNameResult.GetProperty("success").GetBoolean().Should().BeTrue();
        lookupNameResult.GetProperty("bindings")[0].GetProperty("propertyName").GetString().Should().Be("Text");
        lookupNameResult.GetProperty("bindings")[0].GetProperty("path").GetString().Should().Be("Name");
        lookupNameResult.GetProperty("bindings")[0].GetProperty("currentValue").GetString().Should().Be("Alice");

        ageResult.GetProperty("success").GetBoolean().Should().BeTrue();
        ageResult.GetProperty("bindings")[0].GetProperty("propertyName").GetString().Should().Be("Text");
        ageResult.GetProperty("bindings")[0].GetProperty("path").GetString().Should().Be("Age");
        ageResult.GetProperty("bindings")[0].GetProperty("currentValue").GetString().Should().Be("30");

        enabledResult.GetProperty("success").GetBoolean().Should().BeTrue();
        enabledResult.GetProperty("bindings")[0].GetProperty("propertyName").GetString().Should().Be("IsChecked");
        enabledResult.GetProperty("bindings")[0].GetProperty("path").GetString().Should().Be("IsEnabled");
        enabledResult.GetProperty("bindings")[0].GetProperty("currentValue").GetString().Should().Be("False");
    }

    [Fact]
    public void GetDataContextChain_WithTestViewModel_ShouldReturnChain()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectBasicControlsTab(context);

            context.BrokenDetailContextCheckBox.IsChecked = false;
            context.Window.UpdateLayout();

            var detailTextBoxId = elementFinder.GenerateElementId(context.DetailContextTextBox);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetDataContextChain(detailTextBoxId));
            EvictElementCacheEntry(elementFinder, detailTextBoxId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetDataContextChain(detailTextBoxId));

            return JsonSerializer.SerializeToElement(new
            {
                cachedResult,
                lookupResult
            });
        });

        var cachedResult = result.GetProperty("cachedResult");
        var lookupResult = result.GetProperty("lookupResult");
        cachedResult.GetRawText().Should().Be(lookupResult.GetRawText());

        lookupResult.GetProperty("success").GetBoolean().Should().BeTrue();
        lookupResult.GetProperty("chain").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        lookupResult.GetProperty("chain")[0].GetProperty("elementName").GetString().Should().Be("DetailContextTextBox");
        lookupResult.GetProperty("chain")[0].GetProperty("dataContextType").GetString().Should().Be("ValidDetailContext");
        lookupResult.GetProperty("chain")[0].GetProperty("sourceKind").GetString().Should().Be("InheritedDataContext");
        lookupResult.GetProperty("chain")[0].GetProperty("isInherited").GetBoolean().Should().BeTrue();
        lookupResult.GetProperty("chain")[1].GetProperty("elementName").GetString().Should().Be("DynamicDetailHost");
        lookupResult.GetProperty("chain")[1].GetProperty("dataContextType").GetString().Should().Be("ValidDetailContext");
        lookupResult.GetProperty("chain")[1].GetProperty("sourceKind").GetString().Should().Be("LocalDataContext");
        lookupResult.GetProperty("chain")[1].GetProperty("isInherited").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ForceBindingUpdate_WithTestViewModelBinding_ShouldUpdateSuccessfully()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectBasicControlsTab(context);

            var detailContext = context.ViewModel.CurrentDetailContext.Should().BeOfType<ValidDetailContext>().Subject;
            var bindingExpression = context.DetailContextTextBox.GetBindingExpression(TextBox.TextProperty);
            var detailTextBoxId = elementFinder.GenerateElementId(context.DetailContextTextBox);

            detailContext.DetailName = "Detail ready";
            bindingExpression?.UpdateTarget();
            context.DetailContextTextBox.Text = "Updated detail";
            var sourceValueBeforeCachedUpdate = detailContext.DetailName;
            var cachedResult = JsonSerializer.SerializeToElement(
                analyzer.ForceBindingUpdate(detailTextBoxId, propertyName: "Text", direction: "Source"));
            var sourceValueAfterCachedUpdate = detailContext.DetailName;

            detailContext.DetailName = "Detail ready";
            bindingExpression?.UpdateTarget();
            context.DetailContextTextBox.Text = "Updated detail";
            EvictElementCacheEntry(elementFinder, detailTextBoxId);
            var lookupResult = JsonSerializer.SerializeToElement(
                analyzer.ForceBindingUpdate(detailTextBoxId, propertyName: "Text", direction: "Source"));
            var sourceValueAfterLookupUpdate = detailContext.DetailName;

            return JsonSerializer.SerializeToElement(new
            {
                cachedResult,
                lookupResult,
                sourceValueBeforeCachedUpdate,
                sourceValueAfterCachedUpdate,
                sourceValueAfterLookupUpdate
            });
        });

        result.GetProperty("cachedResult").GetRawText().Should().Be(result.GetProperty("lookupResult").GetRawText());
        result.GetProperty("lookupResult").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("lookupResult").GetProperty("direction").GetString().Should().Be("Source");
        result.GetProperty("lookupResult").GetProperty("propertyName").GetString().Should().Be("Text");
        result.GetProperty("sourceValueBeforeCachedUpdate").GetString().Should().Be("Detail ready");
        result.GetProperty("sourceValueAfterCachedUpdate").GetString().Should().Be("Updated detail");
        result.GetProperty("sourceValueAfterLookupUpdate").GetString().Should().Be("Updated detail");
    }

    [Fact]
    public void GetBindingErrors_WithGoldenSampleValidationAndBindingFailures_ShouldExcludeValidationMessages()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            BindingErrorTraceListener.ResetInstance();
            using var elementFinder = new ElementFinder();
            var analyzer = new BindingAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectBasicControlsTab(context);

            context.ViewModel.Name = string.Empty;
            context.ViewModel.Age = 0;
            context.Window.UpdateLayout();

            context.NameTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            context.AgeTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            context.ErrorTextBox1.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            context.ErrorTextBox2.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            context.ErrorTextBox3.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            context.BrushMismatchButton.GetBindingExpression(Button.BackgroundProperty)?.UpdateTarget();

            return JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: false));
        });

        var messages = result.GetProperty("errors")
            .EnumerateArray()
            .Select(error => error.GetProperty("message").GetString())
            .Where(message => message != null)
            .ToArray();
        var bindingPaths = result.GetProperty("errors")
            .EnumerateArray()
            .Select(error => error.GetProperty("bindingPath").GetString())
            .Where(path => path != null)
            .Cast<string>()
            .ToArray();

        var messageSummary = string.Join(" || ", messages);

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(4, because: messageSummary);

        bindingPaths.Should().Contain("InvalidPropertyName");
        bindingPaths.Should().Contain("NonExistent.Property");
        bindingPaths.Should().Contain("Name");
        bindingPaths.Should().Contain("IsEnabled");
        messages.Should().Contain(message => message!.Contains("InvalidPropertyName", StringComparison.Ordinal));
        messages.Should().Contain(message => message!.Contains("NonExistent.Property", StringComparison.Ordinal));
        messages.Should().Contain(message => message!.Contains("no DataContext", StringComparison.OrdinalIgnoreCase)
            || message!.Contains("no DataContext or resolved source", StringComparison.OrdinalIgnoreCase));
        messages.Should().NotContain(message => message!.Contains("Name is required", StringComparison.Ordinal));
        messages.Should().NotContain(message => message!.Contains("Age must be greater than 0", StringComparison.Ordinal));
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private static void SelectBasicControlsTab(TestAppBindingWindowContext context)
    {
        context.MainTabControl.SelectedItem = context.BasicControlsTab;
        context.Window.UpdateLayout();
    }

    private TestAppBindingWindowContext CreateRealTestAppWindow()
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
        var nameTextBox = window.FindName("NameTextBox") as TextBox;
        var ageTextBox = window.FindName("AgeTextBox") as TextBox;
        var enabledCheckBox = window.FindName("EnabledCheckBox") as CheckBox;
        var errorTextBox1 = window.FindName("ErrorTextBox1") as TextBox;
        var errorTextBox2 = window.FindName("ErrorTextBox2") as TextBox;
        var errorTextBox3 = window.FindName("ErrorTextBox3") as TextBox;
        var brushMismatchButton = window.FindName("BrushMismatchButton") as Button;
        var brokenDetailContextCheckBox = window.FindName("BrokenDetailContextCheckBox") as CheckBox;
        var dynamicDetailHost = window.FindName("DynamicDetailHost") as ContentControl;
        var detailContextTextBox = window.FindName("DetailContextTextBox") as TextBox;

        viewModel.Should().NotBeNull();
        mainTabControl.Should().NotBeNull();
        basicControlsTab.Should().NotBeNull();
        nameTextBox.Should().NotBeNull();
        ageTextBox.Should().NotBeNull();
        enabledCheckBox.Should().NotBeNull();
        errorTextBox1.Should().NotBeNull();
        errorTextBox2.Should().NotBeNull();
        errorTextBox3.Should().NotBeNull();
        brushMismatchButton.Should().NotBeNull();
        brokenDetailContextCheckBox.Should().NotBeNull();
        dynamicDetailHost.Should().NotBeNull();
        detailContextTextBox.Should().NotBeNull();

        return new TestAppBindingWindowContext(
            window,
            viewModel!,
            mainTabControl!,
            basicControlsTab!,
            nameTextBox!,
            ageTextBox!,
            enabledCheckBox!,
            errorTextBox1!,
            errorTextBox2!,
            errorTextBox3!,
            brushMismatchButton!,
            brokenDetailContextCheckBox!,
            dynamicDetailHost!,
            detailContextTextBox!);
    }

    private sealed record TestAppBindingWindowContext(
        MainWindow Window,
        TestViewModel ViewModel,
        TabControl MainTabControl,
        TabItem BasicControlsTab,
        TextBox NameTextBox,
        TextBox AgeTextBox,
        CheckBox EnabledCheckBox,
        TextBox ErrorTextBox1,
        TextBox ErrorTextBox2,
        TextBox ErrorTextBox3,
        Button BrushMismatchButton,
        CheckBox BrokenDetailContextCheckBox,
        ContentControl DynamicDetailHost,
        TextBox DetailContextTextBox);
}
