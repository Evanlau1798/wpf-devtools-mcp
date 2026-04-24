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
/// Integration tests using TestApp golden sample custom controls.
/// Tests CustomTextBox (DependencyProperty with coercion, attached property)
/// and CustomButton (custom RoutedEvent) from TestApp Tab 5.
/// </summary>
[Collection("WpfIntegration")]
public sealed class TestAppCustomControlIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private MainWindow? _activeTestAppWindow;

    public TestAppCustomControlIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        _fixture.RunOnUIThread(() =>
        {
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
    public void GetValueSource_WithCustomDependencyProperty_ShouldReturnSource()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectCustomControlsTab(context);

            var elementId = elementFinder.GenerateElementId(context.CustomTextBox1);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Watermark", elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Watermark", elementId));

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
        lookupResult.GetProperty("propertyName").GetString().Should().Be("Watermark");
        lookupResult.GetProperty("baseValueSource").GetString().Should().Be("LocalValue");
        lookupResult.GetProperty("currentValue").GetString().Should().Be("Enter your name");
        lookupResult.GetProperty("hadLocalValue").GetBoolean().Should().BeTrue();
        lookupResult.GetProperty("localValue").GetString().Should().Be("Enter your name");
    }

    [Fact]
    public void GetMetadata_WithCustomDependencyProperty_ShouldReturnMetadata()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectCustomControlsTab(context);

            var elementId = elementFinder.GenerateElementId(context.CustomTextBox1);
            EvictElementCacheEntry(elementFinder, elementId);

            return JsonSerializer.SerializeToElement(analyzer.GetMetadata("Watermark", elementId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyName").GetString().Should().Be("Watermark");
        result.GetProperty("propertyType").GetString().Should().Be("String");
        result.GetProperty("ownerType").GetString().Should().Be("CustomTextBox");
        result.GetProperty("defaultValue").GetString().Should().Be("Enter text...");
        result.GetProperty("isReadOnly").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void SetValue_WithWatermarkCoercion_ShouldCoerceEmptyToDefault()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectCustomControlsTab(context);

            var elementId = elementFinder.GenerateElementId(context.CustomTextBox1);
            EvictElementCacheEntry(elementFinder, elementId);

            return JsonSerializer.SerializeToElement(new
            {
                result = analyzer.SetValue("Watermark", "", elementId),
                actualWatermark = context.CustomTextBox1.Watermark,
                actualToolTip = context.CustomTextBox1.ToolTip?.ToString()
            });
        });

        result.GetProperty("actualWatermark").GetString().Should().Be("Default watermark");
        result.GetProperty("actualToolTip").GetString().Should().Be("Watermark: Default watermark");
        result.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("propertyName").GetString().Should().Be("Watermark");
        result.GetProperty("result").GetProperty("newValue").GetString().Should().Be("Default watermark");
        result.GetProperty("result").GetProperty("baseValueSource").GetString().Should().Be("Local");
    }

    [Theory]
    [InlineData("HighlightColor")]
    [InlineData("CustomTextBox.HighlightColor")]
    public void GetValueSource_WithAttachedProperty_ShouldReturnSource(string propertyName)
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectCustomControlsTab(context);

            var elementId = elementFinder.GenerateElementId(context.AttachedPropTextBox);
            EvictElementCacheEntry(elementFinder, elementId);

            return JsonSerializer.SerializeToElement(analyzer.GetValueSource(propertyName, elementId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue(result.GetRawText());
        result.GetProperty("baseValueSource").GetString().Should().Be("LocalValue");
        result.GetProperty("effectiveValue").GetString().Should().Be("LightYellow");
        result.GetProperty("currentValue").GetString().Should().Be("LightYellow");
        result.GetProperty("hadLocalValue").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("HighlightColor")]
    [InlineData("CustomTextBox.HighlightColor")]
    public void GetMetadata_WithAttachedProperty_ShouldReturnMetadata(string propertyName)
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectCustomControlsTab(context);

            var elementId = elementFinder.GenerateElementId(context.AttachedPropTextBox);
            EvictElementCacheEntry(elementFinder, elementId);

            return JsonSerializer.SerializeToElement(analyzer.GetMetadata(propertyName, elementId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue(result.GetRawText());
        result.GetProperty("propertyName").GetString().Should().Be(propertyName);
        result.GetProperty("propertyType").GetString().Should().Be("String");
        result.GetProperty("ownerType").GetString().Should().Be("CustomTextBox");
        result.GetProperty("defaultValue").GetString().Should().Be("Yellow");
    }

    [Fact]
    public void FireRoutedEvent_WithCustomRoutedEvent_ShouldFireSuccessfully()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new EventAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectCustomControlsTab(context);

            context.ViewModel.ResetStateCommand.Execute(null);
            var elementId = elementFinder.GenerateElementId(context.CustomButton1);
            var cachedResult = JsonSerializer.SerializeToElement(
                analyzer.FireRoutedEvent(elementId: elementId, eventName: "CustomClick", eventArgs: null));
            var automationStatusAfterCachedFire = context.AutomationStatusTextBlock.Text;

            context.ViewModel.ResetStateCommand.Execute(null);
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(
                analyzer.FireRoutedEvent(elementId: elementId, eventName: "CustomClick", eventArgs: null));
            var automationStatusAfterLookupFire = context.AutomationStatusTextBlock.Text;

            return JsonSerializer.SerializeToElement(new
            {
                cachedResult,
                lookupResult,
                automationStatusAfterCachedFire,
                automationStatusAfterLookupFire
            });
        });

        result.GetProperty("cachedResult").GetRawText().Should().Be(result.GetProperty("lookupResult").GetRawText());
        result.GetProperty("lookupResult").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("automationStatusAfterCachedFire").GetString().Should().Be("Custom routed event fired!");
        result.GetProperty("automationStatusAfterLookupFire").GetString().Should().Be("Custom routed event fired!");
    }

    [Fact]
    public void GetEventHandlers_WithCustomRoutedEvent_ShouldReturnHandlers()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new EventAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectCustomControlsTab(context);

            var elementId = elementFinder.GenerateElementId(context.CustomButton1);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId: elementId, eventName: "CustomClick"));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId: elementId, eventName: "CustomClick"));

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
        lookupResult.GetProperty("handlerCount").GetInt32().Should().BeGreaterThan(0);
        lookupResult.GetProperty("handlers").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void ClearValue_WithCustomDependencyProperty_ShouldRevertToDefault()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectCustomControlsTab(context);

            var elementId = elementFinder.GenerateElementId(context.CustomTextBox1);
            EvictElementCacheEntry(elementFinder, elementId);

            return JsonSerializer.SerializeToElement(new
            {
                result = analyzer.ClearValue("Watermark", elementId),
                actualWatermark = context.CustomTextBox1.Watermark,
                hasLocalValue = context.CustomTextBox1.ReadLocalValue(CustomTextBox.WatermarkProperty) != DependencyProperty.UnsetValue
            });
        });

        result.GetProperty("actualWatermark").GetString().Should().Be("Enter text...");
        result.GetProperty("hasLocalValue").GetBoolean().Should().BeFalse();
        result.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("propertyName").GetString().Should().Be("Watermark");
        result.GetProperty("result").GetProperty("hadLocalValue").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("baseValueSource").GetString().Should().Be("Default");
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private static void SelectCustomControlsTab(TestAppCustomControlWindowContext context)
    {
        context.MainTabControl.SelectedItem = context.CustomControlsTab;
        context.Window.UpdateLayout();
        context.CustomTextBox1.ApplyTemplate();
        context.CustomTextBox2.ApplyTemplate();
        context.Window.UpdateLayout();
    }

    private TestAppCustomControlWindowContext CreateRealTestAppWindow()
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
        var automationStatusTextBlock = window.FindName("AutomationStatusTextBlock") as TextBlock;
        var customTextBox1 = window.FindName("CustomTextBox1") as CustomTextBox;
        var customTextBox2 = window.FindName("CustomTextBox2") as CustomTextBox;
        var attachedPropTextBox = window.FindName("AttachedPropTextBox") as TextBox;
        var customButton1 = window.FindName("CustomButton1") as CustomButton;

        mainTabControl.Should().NotBeNull();
        viewModel.Should().NotBeNull();
        automationStatusTextBlock.Should().NotBeNull();
        customTextBox1.Should().NotBeNull();
        customTextBox2.Should().NotBeNull();
        attachedPropTextBox.Should().NotBeNull();
        customButton1.Should().NotBeNull();

        var customControlsTab = mainTabControl!.Items
            .OfType<TabItem>()
            .SingleOrDefault(tabItem => Equals(tabItem.Header, "Custom Controls"));
        customControlsTab.Should().NotBeNull();

        return new TestAppCustomControlWindowContext(
            window,
            viewModel!,
            mainTabControl,
            customControlsTab!,
            automationStatusTextBlock!,
            customTextBox1!,
            customTextBox2!,
            attachedPropTextBox!,
            customButton1!);
    }

    private sealed record TestAppCustomControlWindowContext(
        MainWindow Window,
        TestViewModel ViewModel,
        TabControl MainTabControl,
        TabItem CustomControlsTab,
        TextBlock AutomationStatusTextBlock,
        CustomTextBox CustomTextBox1,
        CustomTextBox CustomTextBox2,
        TextBox AttachedPropTextBox,
        CustomButton CustomButton1);
}
