using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
using WpfUiFluentWindow = Wpf.Ui.Controls.FluentWindow;
using WpfUiNavigationView = Wpf.Ui.Controls.NavigationView;
using WpfUiNavigationViewItem = Wpf.Ui.Controls.NavigationViewItem;

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
            var navigation = descendants.OfType<WpfUiNavigationView>().Should().ContainSingle().Subject;
            ((System.Collections.ICollection)navigation.MenuItems).Count.Should().Be(4);
            ((System.Collections.ICollection)navigation.FooterMenuItems).Count.Should().Be(1);
            descendants.OfType<WpfUiNavigationViewItem>()
                .Select(item => item.TargetPageTag)
                .Should().Contain(["overview", "workspace", "activity", "reports", "settings"]);
            descendants.OfType<WpfUiAutoSuggestBox>().Should().ContainSingle(box =>
                string.Equals(box.PlaceholderText, "Search", StringComparison.Ordinal));
            var primaryAction = descendants.OfType<WpfUiButton>().Should().ContainSingle(button =>
                string.Equals(button.Content as string, "Open Incident Log", StringComparison.Ordinal)).Subject;

            var visibleText = string.Join(" ", descendants.Select(GetDisplayText));
            visibleText.Should().ContainAll(
                "Berth Board", "Tide Watch", "Pilot Roster", "Manifest Desk",
                "Live Berth Operations", "Open Incident Log");
            visibleText.Should().NotContainAny("All Controls", "Basic input", "NavigationView Header");

            AssertImplicitStyleResource<WpfUiNavigationView>(window);
            AssertImplicitStyleResource<WpfUiButton>(window);
            AssertImplicitStyleResource<WpfUiAutoSuggestBox>(window);
            AssertStyleAnalyzerReportsImplicitWpfUiStyle(primaryAction);
        }
        finally
        {
            window.Close();
        }
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
