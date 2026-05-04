using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for StyleAnalyzer using TestApp golden sample scenarios.
/// Tests style inheritance, triggers, and templates matching TestApp Tab 4
/// (Styles &amp; Templates).
/// </summary>
[Collection("WpfAndBootstrapIntegration")]
public class TestAppStyleIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private MainWindow? _activeTestAppWindow;

    public TestAppStyleIntegrationTests(WpfApplicationFixture fixture)
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
    public void GetAppliedStyles_WithInheritedStyle_ShouldReturnStyleInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectStylesTab(context);

            var buttonId = elementFinder.GenerateElementId(context.PrimaryStyleButton);
            EvictElementCacheEntry(elementFinder, buttonId);

            return JsonSerializer.SerializeToElement(analyzer.GetAppliedStyles(buttonId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasStyle").GetBoolean().Should().BeTrue();
        result.GetProperty("count").GetInt32().Should().Be(1);
        result.GetProperty("styleCount").GetInt32().Should().Be(1);
        result.GetProperty("localResourceReferenceCount").GetInt32().Should().Be(0);
        result.GetProperty("styles").EnumerateArray().Should().ContainSingle(style =>
            style.GetProperty("styleType").GetString() == "Explicit"
            && style.GetProperty("targetType").GetString() == "Button"
            && style.GetProperty("setterCount").GetInt32() == 2
            && style.GetProperty("triggerCount").GetInt32() == 2
            && style.GetProperty("hasBasedOn").GetBoolean());
        result.GetProperty("styles").EnumerateArray().Should().ContainSingle(style =>
            style.GetProperty("setters").EnumerateArray().Any(setter =>
                setter.GetProperty("property").GetString() == "Background"));
        result.GetProperty("styles").EnumerateArray().Should().ContainSingle(style =>
            style.GetProperty("setters").EnumerateArray().Any(setter =>
                setter.GetProperty("property").GetString() == "Foreground"));
    }

    [Fact]
    public void GetTriggers_WithStyleTriggers_ShouldReturnTriggerInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectStylesTab(context);

            var buttonId = elementFinder.GenerateElementId(context.PrimaryStyleButton);
            EvictElementCacheEntry(elementFinder, buttonId);

            return JsonSerializer.SerializeToElement(analyzer.GetTriggers(buttonId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var styleTriggers = result.GetProperty("triggers")
            .EnumerateArray()
            .Where(trigger => trigger.GetProperty("source").GetString() == "Style")
            .ToArray();

        styleTriggers.Should().HaveCount(2);
        styleTriggers.Should().Contain(trigger =>
            trigger.GetProperty("source").GetString() == "Style"
            && trigger.GetProperty("type").GetString() == "Trigger"
            && trigger.GetProperty("triggerType").GetString() == "Property"
            && trigger.GetProperty("conditions").EnumerateArray().Any(condition =>
                condition.GetProperty("property").GetString() == "IsMouseOver"
                && condition.GetProperty("value").GetString() == "True")
            && trigger.GetProperty("setters").EnumerateArray().Any(setter =>
                setter.GetProperty("property").GetString() == "Background"));
        styleTriggers.Should().Contain(trigger =>
            trigger.GetProperty("source").GetString() == "Style"
            && trigger.GetProperty("conditions").EnumerateArray().Any(condition =>
                condition.GetProperty("property").GetString() == "IsEnabled"
                && condition.GetProperty("value").GetString() == "False")
            && trigger.GetProperty("setters").EnumerateArray().Any(setter =>
                setter.GetProperty("property").GetString() == "Opacity"
                && setter.GetProperty("value").GetString() == "0.5"));
    }

    [Fact]
    public void GetTemplateTree_WithCustomTemplate_ShouldReturnTemplateInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectStylesTab(context);
            context.RoundTemplateButton.ApplyTemplate();

            var buttonId = elementFinder.GenerateElementId(context.RoundTemplateButton);
            EvictElementCacheEntry(elementFinder, buttonId);

            return JsonSerializer.SerializeToElement(analyzer.GetTemplateTree(buttonId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasTemplate").GetBoolean().Should().BeTrue();
        result.GetProperty("templateType").GetString().Should().Be("ControlTemplate");
        result.GetProperty("rootType").GetString().Should().Be("Border");
    }

    [Fact]
    public void GetTriggers_WithControlTemplateTriggers_ShouldReturnTemplateTriggerInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);

            var context = CreateRealTestAppWindow();
            context.CustomTextBox1.ApplyTemplate();

            var textBoxId = elementFinder.GenerateElementId(context.CustomTextBox1);
            EvictElementCacheEntry(elementFinder, textBoxId);

            return JsonSerializer.SerializeToElement(analyzer.GetTriggers(textBoxId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("triggerCount").GetInt32().Should().Be(1);
        result.GetProperty("triggers").EnumerateArray().Should().Contain(trigger =>
            trigger.GetProperty("source").GetString() == "ControlTemplate"
            && trigger.GetProperty("type").GetString() == "MultiTrigger"
            && trigger.GetProperty("triggerType").GetString() == "MultiTrigger"
            && trigger.GetProperty("conditions").EnumerateArray().Any(condition =>
                condition.GetProperty("property").GetString() == "Text"
                && condition.GetProperty("value").GetString() == string.Empty)
            && trigger.GetProperty("conditions").EnumerateArray().Any(condition =>
                condition.GetProperty("property").GetString() == "IsFocused"
                && condition.GetProperty("value").GetString() == "False")
            && trigger.GetProperty("setters").EnumerateArray().Any(setter =>
                setter.GetProperty("property").GetString() == "Visibility"
                && setter.GetProperty("value").GetString() == "Visible"));
    }

    [Fact]
    public void GetResourceChain_WithElementResource_ShouldFindResource()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectStylesTab(context);

            var buttonId = elementFinder.GenerateElementId(context.PrimaryStyleButton);
            EvictElementCacheEntry(elementFinder, buttonId);

            return JsonSerializer.SerializeToElement(analyzer.GetResourceChain(buttonId, resourceKey: "PrimaryButtonStyle"));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("resourceKey").GetString().Should().Be("PrimaryButtonStyle");
        result.GetProperty("found").GetBoolean().Should().BeTrue();
        result.GetProperty("chain").GetArrayLength().Should().Be(1);
        result.GetProperty("chain")[0].GetProperty("level").GetString().Should().Be("Element");
        result.GetProperty("chain")[0].GetProperty("elementType").GetString().Should().Be("TabItem");
        result.GetProperty("chain")[0].GetProperty("resourceType").GetString().Should().Be("Style");
    }

    [Fact]
    public void GetTriggers_WithDataTrigger_ShouldReturnTriggerInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectStylesTab(context);

            context.EnableHighlightCheckBox.IsChecked = true;
            context.HighlightTextBlock.UpdateLayout();
            context.Window.UpdateLayout();

            var textBlockId = elementFinder.GenerateElementId(context.HighlightTextBlock);
            EvictElementCacheEntry(elementFinder, textBlockId);

            return JsonSerializer.SerializeToElement(new
            {
                result = analyzer.GetTriggers(textBlockId),
                foreground = context.HighlightTextBlock.Foreground.ToString(),
                fontWeight = context.HighlightTextBlock.FontWeight.ToString()
            });
        });

        result.GetProperty("foreground").GetString().Should().Be("#FFFF0000");
        result.GetProperty("fontWeight").GetString().Should().Be("Bold");
        result.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("triggerCount").GetInt32().Should().Be(1);
        result.GetProperty("result").GetProperty("triggers").EnumerateArray().Should().Contain(trigger =>
            trigger.GetProperty("source").GetString() == "Style"
            && trigger.GetProperty("type").GetString() == "DataTrigger"
            && trigger.GetProperty("triggerType").GetString() == "Data"
            && trigger.GetProperty("conditions").GetArrayLength() == 1
            && trigger.GetProperty("conditions")[0].GetProperty("property").GetString() == "IsChecked"
            && trigger.GetProperty("conditions")[0].GetProperty("bindingPath").GetString() == "IsChecked"
            && trigger.GetProperty("conditions")[0].GetProperty("bindingElementName").GetString() == "EnableHighlightCheckBox"
            && trigger.GetProperty("conditions")[0].GetProperty("bindingSourceKind").GetString() == "ElementName"
            && trigger.GetProperty("setters").EnumerateArray().Any(setter =>
                setter.GetProperty("property").GetString() == "Foreground")
            && trigger.GetProperty("setters").EnumerateArray().Any(setter =>
                setter.GetProperty("property").GetString() == "FontWeight"
                && setter.GetProperty("value").GetString() == "Bold"));
    }

    [Fact]
    public void GetAppliedStyles_WithDisabledButton_ShouldReturnStyleMetadataForDisabledControl()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectStylesTab(context);

            var buttonId = elementFinder.GenerateElementId(context.PrimaryDisabledButton);
            EvictElementCacheEntry(elementFinder, buttonId);

            return JsonSerializer.SerializeToElement(new
            {
                result = analyzer.GetAppliedStyles(buttonId),
                context.PrimaryDisabledButton.IsEnabled
            });
        });

        result.GetProperty("IsEnabled").GetBoolean().Should().BeFalse();
        result.GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("hasStyle").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("styles").EnumerateArray().Should().ContainSingle(style =>
            style.GetProperty("targetType").GetString() == "Button"
            && style.GetProperty("triggerCount").GetInt32() == 2
            && style.GetProperty("hasBasedOn").GetBoolean());
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private void SelectStylesTab(TestAppStyleWindowContext context)
    {
        context.MainTabControl.SelectedItem = context.StylesTemplatesTab;
        context.Window.UpdateLayout();
    }

    private TestAppStyleWindowContext CreateRealTestAppWindow()
    {
        var application = Application.Current;
        application.Should().NotBeNull();

        _previousMainWindow ??= application!.MainWindow;

        var window = new MainWindow();
        _activeTestAppWindow = window;
        application.MainWindow = window;
        window.Show();
        window.UpdateLayout();

        var mainTabControl = window.FindName("MainTabControl") as TabControl;
        var stylesTemplatesTab = window.FindName("StylesTemplatesTab") as TabItem;
        var primaryStyleButton = window.FindName("PrimaryStyleButton") as Button;
        var primaryDisabledButton = window.FindName("PrimaryDisabledButton") as Button;
        var roundTemplateButton = window.FindName("RoundTemplateButton") as Button;
        var enableHighlightCheckBox = window.FindName("EnableHighlightCheckBox") as CheckBox;
        var highlightTextBlock = window.FindName("HighlightTextBlock") as TextBlock;
        var customTextBox1 = window.FindName("CustomTextBox1") as CustomTextBox;

        mainTabControl.Should().NotBeNull();
        stylesTemplatesTab.Should().NotBeNull();
        primaryStyleButton.Should().NotBeNull();
        primaryDisabledButton.Should().NotBeNull();
        roundTemplateButton.Should().NotBeNull();
        enableHighlightCheckBox.Should().NotBeNull();
        highlightTextBlock.Should().NotBeNull();
        customTextBox1.Should().NotBeNull();

        return new TestAppStyleWindowContext(
            window,
            mainTabControl!,
            stylesTemplatesTab!,
            primaryStyleButton!,
            primaryDisabledButton!,
            roundTemplateButton!,
            enableHighlightCheckBox!,
            highlightTextBlock!,
            customTextBox1!);
    }

    private sealed record TestAppStyleWindowContext(
        MainWindow Window,
        TabControl MainTabControl,
        TabItem StylesTemplatesTab,
        Button PrimaryStyleButton,
        Button PrimaryDisabledButton,
        Button RoundTemplateButton,
        CheckBox EnableHighlightCheckBox,
        TextBlock HighlightTextBlock,
        CustomTextBox CustomTextBox1);
}
