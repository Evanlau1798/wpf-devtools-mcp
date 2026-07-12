using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using WpfUiAutoSuggestBox = Wpf.Ui.Controls.AutoSuggestBox;
using WpfUiButton = Wpf.Ui.Controls.Button;
using WpfUiCard = Wpf.Ui.Controls.Card;
using WpfUiFluentWindow = Wpf.Ui.Controls.FluentWindow;
using WpfUiNavigationView = Wpf.Ui.Controls.NavigationView;
using WpfUiNavigationViewItem = Wpf.Ui.Controls.NavigationViewItem;
using WpfUiNumberBox = Wpf.Ui.Controls.NumberBox;
using WpfUiProgressRing = Wpf.Ui.Controls.ProgressRing;
using WpfUiTabView = Wpf.Ui.Controls.TabView;
using WpfUiTabViewItem = Wpf.Ui.Controls.TabViewItem;
using WpfUiTitleBar = Wpf.Ui.Controls.TitleBar;
using WpfUiToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("WPF")]
public sealed class ComposerRealWpfUiRuntimeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [StaFact]
    public void ShellRecipeGeneratedXaml_ShouldLoadRealWpfUiControlsAndStyles()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        var inputs = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["title"] = "HarborOps Console",
            ["navigationItem1Text"] = "Berth Board",
            ["navigationItem2Text"] = "Tide Watch",
            ["navigationItem3Text"] = "Pilot Roster",
            ["navigationItem4Text"] = "Manifest Desk",
            ["contentHeading"] = "Live Berth Operations",
            ["contentBody"] = "Coordinate pilots, tides, manifests, and active incidents.",
            ["primaryActionText"] = "Open Incident Log"
        });
        var recipe = new RecipeExpansionService(registry)
            .Expand(new RecipeExpansionRequest("wpfui.shellWithNavigation", inputs));
        recipe.Success.Should().BeTrue();
        var render = new UiBlueprintRenderer(registry).Render(
            new RenderBlueprintRequest(JsonSerializer.Serialize(recipe.Blueprint, JsonOptions)));
        render.Success.Should().BeTrue(string.Join(Environment.NewLine, render.Errors.Select(error => error.Message)));
        render.RequiredResources.Should().Contain(resource => resource.Contains("ThemesDictionary Theme=\"Dark\"", StringComparison.Ordinal));
        render.RequiredResources.Should().Contain(resource => resource.Contains("ControlsDictionary", StringComparison.Ordinal));

        var window = (WpfUiFluentWindow)XamlReader.Parse(render.Xaml);
        AddWpfUiResourcesFromPlan(window, render.RequiredResources);

        try
        {
            window.Width.Should().Be(1280);
            window.Height.Should().Be(760);
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveScreenshotIfRequested(window);

            var descendants = EnumerateDescendants(window).ToArray();
            var iconBindings = descendants
                .OfType<WpfUiButton>()
                .Select(button => BindingOperations.GetBindingExpression(button, WpfUiButton.IconProperty))
                .Where(binding => binding is not null)
                .Cast<BindingExpression>()
                .ToArray();
            var navigation = descendants.OfType<WpfUiNavigationView>().Should().ContainSingle().Subject;
            navigation.AutoSuggestBox.Should().NotBeNull();
            var contentCard = descendants.OfType<WpfUiCard>().Should().ContainSingle().Subject;
            ((System.Collections.ICollection)navigation.MenuItems).Count.Should().Be(4);
            ((System.Collections.ICollection)navigation.FooterMenuItems).Count.Should().Be(1);
            descendants.OfType<WpfUiNavigationViewItem>()
                .Select(item => item.TargetPageTag)
                .Should().Contain(["overview", "workspace", "activity", "reports", "settings"]);
            descendants.OfType<WpfUiAutoSuggestBox>().Should().ContainSingle(box =>
                string.Equals(box.PlaceholderText, "Search", StringComparison.Ordinal));
            var primaryAction = descendants.OfType<WpfUiButton>().Should().ContainSingle(button =>
                string.Equals(button.Content as string, "Open Incident Log", StringComparison.Ordinal)).Subject;
            primaryAction.IsEnabled.Should().BeTrue();
            AssertVisibleWithinWindow(window, navigation, minimumWidth: 200, minimumHeight: 200);
            AssertVisibleWithinWindow(window, contentCard, minimumWidth: 200, minimumHeight: 80);
            AssertVisibleWithinWindow(window, primaryAction, minimumWidth: 20, minimumHeight: 20);

            var visibleText = string.Join(" ", descendants.Select(GetDisplayText));
            visibleText.Should().ContainAll(
                "Berth Board", "Tide Watch", "Pilot Roster", "Manifest Desk",
                "Live Berth Operations", "Open Incident Log");
            visibleText.Should().NotContainAny("All Controls", "Basic input", "NavigationView Header");

            AssertImplicitStyleResource<WpfUiNavigationView>(window);
            AssertImplicitStyleResource<WpfUiButton>(window);
            AssertImplicitStyleResource<WpfUiAutoSuggestBox>(window);
            AssertStyleAnalyzerReportsImplicitWpfUiStyle(primaryAction);
            iconBindings.Should().NotBeEmpty();
            iconBindings.Select(binding => binding.Status).Should().NotContain(BindingStatus.PathError);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void VisualFoundationControls_ShouldLoadWithRealWpfUiTypes()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        var blueprint = """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "VisualFoundationControls",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "wpfui.fluentWindow",
                "slots": { "content": [{
                  "kind": "core.stack",
                  "slots": { "children": [
                    { "kind": "wpfui.numberBox", "properties": { "value": 42, "minimum": 0, "maximum": 100, "smallChange": 5 } },
                    { "kind": "wpfui.toggleSwitch", "properties": { "isChecked": true, "offContent": "Off", "onContent": "On", "labelPosition": "Right" } },
                    { "kind": "wpfui.progressRing", "properties": { "progress": 65, "isIndeterminate": false, "size": 32 } }
                  ] }
                }] }
              }
            }
            """;
        var render = new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest(blueprint));

        render.Success.Should().BeTrue(string.Join(Environment.NewLine, render.Errors.Select(error => error.Message)));
        var window = (WpfUiFluentWindow)XamlReader.Parse(render.Xaml);
        AddWpfUiResourcesFromPlan(window, render.RequiredResources);
        var descendants = EnumerateDescendants(window).ToArray();

        descendants.OfType<WpfUiNumberBox>().Should().ContainSingle(box => box.Value == 42);
        descendants.OfType<WpfUiToggleSwitch>().Should().ContainSingle(toggle => toggle.IsChecked == true);
        descendants.OfType<WpfUiProgressRing>().Should().ContainSingle(ring => ring.Progress == 65);
    }

    [StaFact]
    public void TitleBarActions_ShouldLoadThroughRealWpfUiTrailingContent()
    {
        var render = Render("""
            {
              "kind": "wpfui.fluentWindow",
              "slots": {
                "titleBar": [{
                  "kind": "wpfui.titleBar",
                  "properties": { "title": "Studio" },
                  "slots": { "actions": [{ "kind": "wpfui.button", "properties": { "text": "History" } }] }
                }],
                "content": [{ "kind": "core.text", "properties": { "text": "Ready" } }]
              }
            }
            """);

        render.Success.Should().BeTrue(string.Join(Environment.NewLine, render.Errors.Select(error => error.Message)));
        var window = (WpfUiFluentWindow)XamlReader.Parse(render.Xaml);
        var titleBar = EnumerateDescendants(window).OfType<WpfUiTitleBar>().Should().ContainSingle().Subject;

        titleBar.TrailingContent.Should().BeOfType<StackPanel>()
            .Which.Children.OfType<WpfUiButton>().Should().ContainSingle(button =>
                string.Equals(button.Content as string, "History", StringComparison.Ordinal));
    }

    [StaFact]
    public void TabViewItems_ShouldLoadWithoutUnsupportedProperties()
    {
        var render = Render("""
            {
              "kind": "wpfui.fluentWindow",
              "slots": { "content": [{
                "kind": "wpfui.tabView",
                "slots": { "items": [{
                  "kind": "wpfui.tabViewItem",
                  "slots": {
                    "header": [{ "kind": "core.text", "properties": { "text": "General" } }],
                    "content": [{ "kind": "core.text", "properties": { "text": "Ready" } }]
                  }
                }] }
              }] }
            }
            """);

        render.Success.Should().BeTrue(string.Join(Environment.NewLine, render.Errors.Select(error => error.Message)));
        render.RequiredResources.Should().Equal(
            "<ui:ThemesDictionary Theme=\"Dark\" />",
            "<ui:ControlsDictionary />");
        var window = (WpfUiFluentWindow)XamlReader.Parse(render.Xaml);
        AddWpfUiResourcesFromPlan(window, render.RequiredResources);

        try
        {
            window.Show();
            window.UpdateLayout();
            var tabView = EnumerateDescendants(window).OfType<WpfUiTabView>().Should().ContainSingle().Subject;
            tabView.IsVisible.Should().BeTrue();
            tabView.Style.Should().BeSameAs(window.FindResource(typeof(TabControl)));
            tabView.Background.Should().BeOfType<SolidColorBrush>()
                .Which.Color.A.Should().Be(0);
            tabView.Items.OfType<WpfUiTabViewItem>().Should().ContainSingle()
                .Which.Style.Should().BeSameAs(window.FindResource(typeof(TabItem)));
        }
        finally
        {
            window.Close();
        }
    }

    private static RenderBlueprintResult Render(string layout)
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        return new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest($$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "RealWpfUiContract",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {{layout}}
            }
            """));
    }

    private static void AssertVisibleWithinWindow(
        FrameworkElement window,
        FrameworkElement element,
        double minimumWidth,
        double minimumHeight)
    {
        element.IsVisible.Should().BeTrue();
        element.ActualWidth.Should().BeGreaterThanOrEqualTo(minimumWidth);
        element.ActualHeight.Should().BeGreaterThanOrEqualTo(minimumHeight);
        var origin = element.TransformToAncestor(window).Transform(new Point());
        origin.X.Should().BeGreaterThanOrEqualTo(0);
        origin.Y.Should().BeGreaterThanOrEqualTo(0);
        (origin.X + element.ActualWidth).Should().BeLessThanOrEqualTo(window.ActualWidth + 1);
        (origin.Y + element.ActualHeight).Should().BeLessThanOrEqualTo(window.ActualHeight + 1);
    }

    private static void AssertImplicitStyleResource<TControl>(FrameworkElement element)
        where TControl : FrameworkElement
    {
        element.TryFindResource(typeof(TControl)).Should().BeOfType<Style>()
            .Which.TargetType.Should().Be(typeof(TControl));
    }

    private static void AssertStyleAnalyzerReportsImplicitWpfUiStyle(WpfUiButton button)
    {
        using var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var elementId = finder.GenerateElementId(button);
        var styleInfo = JsonSerializer.SerializeToElement(analyzer.GetAppliedStyles(elementId, compact: true));
        var appliedStyle = styleInfo.GetProperty("styles")[0];

        appliedStyle.GetProperty("styleType").GetString().Should().Be("Implicit");
        appliedStyle.GetProperty("baseValueSource").GetString().Should().Be("ImplicitStyleReference");
        appliedStyle.GetProperty("targetTypeFullName").GetString().Should().Be(typeof(WpfUiButton).FullName);
    }

    private static void AddWpfUiResourcesFromPlan(FrameworkElement element, IReadOnlyList<string> resources)
    {
        foreach (var resource in resources)
        {
            if (resource.Contains("ThemesDictionary", StringComparison.Ordinal))
            {
                var theme = resource.Contains("Theme=\"Dark\"", StringComparison.Ordinal)
                    ? ApplicationTheme.Dark
                    : ApplicationTheme.Light;
                element.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = theme });
            }
            else if (resource.Contains("ControlsDictionary", StringComparison.Ordinal))
            {
                element.Resources.MergedDictionaries.Add(new ControlsDictionary());
            }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        var seen = new HashSet<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;
            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
            {
                stack.Push(child);
            }

            if (current is not Visual and not Visual3D)
            {
                continue;
            }

            for (var i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--)
            {
                stack.Push(VisualTreeHelper.GetChild(current, i));
            }
        }
    }

    private static string GetDisplayText(DependencyObject element)
        => element switch
        {
            TextBlock textBlock => textBlock.Text,
            ContentControl { Content: string text } => text,
            HeaderedContentControl { Header: string text } => text,
            _ => string.Empty
        };

    private static void SaveScreenshotIfRequested(FrameworkElement element)
    {
        var directory = Environment.GetEnvironmentVariable("WPFDEVTOOLS_COMPOSER_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, "composer-real-wpfui-shell.png"));
        encoder.Save(stream);
    }
}
