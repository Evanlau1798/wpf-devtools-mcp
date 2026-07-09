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
using WpfUiSymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using WpfUiTitleBar = Wpf.Ui.Controls.TitleBar;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("WPF")]
public sealed class ComposerRealWpfUiRuntimeTests
{
    private const double GalleryReferenceWidth = 1828;
    private const double GalleryReferenceHeight = 962;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [StaFact]
    public void ShellRecipeGeneratedXaml_ShouldLoadRealWpfUiControlsAndStyles()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        var recipe = new RecipeExpansionService(registry)
            .Expand(new RecipeExpansionRequest("wpfui.shellWithNavigation"));
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
            window.Width.Should().Be(GalleryReferenceWidth);
            window.Height.Should().Be(GalleryReferenceHeight);
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveScreenshotIfRequested(window);

            var descendants = EnumerateDescendants(window).ToArray();
            var navigationViews = descendants.OfType<WpfUiNavigationView>().ToArray();
            var outerNavigationView = navigationViews.Should().ContainSingle(view =>
                ((System.Collections.ICollection)view.MenuItems).Count >= 10).Which;
            var demoNavigationView = navigationViews.Should().ContainSingle(view =>
                ((System.Collections.ICollection)view.MenuItems).Count == 3).Which;
            outerNavigationView.PaneDisplayMode.ToString().Should().Be("Left");
            demoNavigationView.PaneDisplayMode.ToString().Should().Be("Left");
            ((System.Collections.ICollection)outerNavigationView.FooterMenuItems).Count.Should().Be(2);
            descendants.OfType<WpfUiNavigationViewItem>().Should().Contain(item =>
                string.Equals(item.TargetPageTag, "settings", StringComparison.Ordinal));
            descendants.OfType<WpfUiSymbolIcon>().Should().Contain(icon =>
                string.Equals(icon.Symbol.ToString(), "Home24", StringComparison.Ordinal));
            descendants.OfType<WpfUiSymbolIcon>().Should().Contain(icon =>
                string.Equals(icon.Symbol.ToString(), "List24", StringComparison.Ordinal));
            descendants.OfType<WpfUiSymbolIcon>().Should().Contain(icon =>
                string.Equals(icon.Symbol.ToString(), "Settings24", StringComparison.Ordinal));
            descendants.OfType<WpfUiAutoSuggestBox>().Should().Contain(box =>
                string.Equals(box.PlaceholderText, "Search", StringComparison.Ordinal));
            var copyButton = descendants.OfType<WpfUiButton>().Should().ContainSingle(button =>
                string.Equals(button.Content as string, "Copy", StringComparison.Ordinal)).Which;
            copyButton.Appearance.ToString().Should().Be("Secondary");
            AssertOuterShellMatchesGalleryReference(window, outerNavigationView, descendants);
            AssertGalleryLikeLayout(window, outerNavigationView, demoNavigationView, copyButton, descendants);
            descendants.OfType<System.Windows.Controls.Button>().Should().NotContain(button =>
                button.GetType() == typeof(System.Windows.Controls.Button)
                && string.Equals(button.Content as string, "Copy", StringComparison.Ordinal));

            var visibleText = string.Join(" ", descendants.Select(GetDisplayText));
            visibleText.Should().Contain("All Controls");
            visibleText.Should().Contain("Basic input");
            visibleText.Should().Contain("Collections");
            visibleText.Should().Contain("Home");
            visibleText.Should().Contain("Items");
            visibleText.Should().Contain("Dashboard");
            visibleText.Should().Contain("Pane Header");
            visibleText.Should().Contain("Pane Footer");
            visibleText.Should().Contain("Settings");
            visibleText.Should().Contain("NavigationView");
            visibleText.Should().Contain("NavigationView Header");
            visibleText.Should().Contain("WPF UI NavigationView.");
            visibleText.Should().Contain("<ui:NavigationView IsBackButtonVisible=\"Auto\">");
            AssertColorTileVisible(descendants, Color.FromRgb(0x7A, 0xF5, 0xD3));
            AssertColorTileVisible(descendants, Color.FromRgb(0x88, 0x88, 0x86));
            AssertColorTileVisible(descendants, Color.FromRgb(0xFF, 0x87, 0x00));

            AssertImplicitStyleResource<WpfUiNavigationView>(window);
            AssertImplicitStyleResource<WpfUiButton>(window);
            AssertImplicitStyleResource<WpfUiAutoSuggestBox>(window);
            AssertStyleAnalyzerReportsImplicitWpfUiStyle(copyButton);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertGalleryLikeLayout(
        FrameworkElement root,
        FrameworkElement outerNavigationView,
        FrameworkElement demoNavigationView,
        FrameworkElement copyButton,
        IReadOnlyList<DependencyObject> descendants)
    {
        outerNavigationView.IsVisible.Should().BeTrue();
        demoNavigationView.IsVisible.Should().BeTrue();
        copyButton.IsVisible.Should().BeTrue();
        root.ActualWidth.Should().BeApproximately(GalleryReferenceWidth, 2);
        root.ActualHeight.Should().BeApproximately(GalleryReferenceHeight, 2);
        outerNavigationView.ActualWidth.Should().BeGreaterThan(220);
        demoNavigationView.ActualWidth.Should().BeGreaterThan(220);
        copyButton.ActualWidth.Should().BeGreaterThan(40);

        var mainTitle = FindTextBlock(descendants, "NavigationView");
        var demoHeader = FindTextBlock(descendants, "NavigationView Header");
        var outerLeft = outerNavigationView.TransformToAncestor(root).Transform(new Point()).X;
        var titleLeft = mainTitle.TransformToAncestor(root).Transform(new Point()).X;
        var demoLeft = demoNavigationView.TransformToAncestor(root).Transform(new Point()).X;
        var demoHeaderLeft = demoHeader.TransformToAncestor(root).Transform(new Point()).X;
        var demoCard = FindSolidBorder(descendants, Color.FromRgb(0x30, 0x34, 0x3E));
        var primaryTile = FindSolidBorder(descendants, Color.FromRgb(0x7A, 0xF5, 0xD3));
        var secondaryTile = FindSolidBorder(descendants, Color.FromRgb(0x88, 0x88, 0x86));

        demoCard.ActualWidth.Should().BeInRange(1290, 1320);
        demoCard.ActualHeight.Should().BeGreaterThan(470);
        primaryTile.ActualWidth.Should().BeGreaterThan(380);
        primaryTile.ActualHeight.Should().BeGreaterThan(290);
        secondaryTile.ActualWidth.Should().BeGreaterThan(380);
        titleLeft.Should().BeGreaterThan(outerLeft + outerNavigationView.ActualWidth * 0.75);
        demoHeaderLeft.Should().BeGreaterThan(demoLeft + demoNavigationView.ActualWidth * 0.75);
    }

    private static void AssertOuterShellMatchesGalleryReference(
        FrameworkElement root,
        FrameworkElement outerNavigationView,
        IReadOnlyList<DependencyObject> descendants)
    {
        descendants.OfType<WpfUiTitleBar>().Should().ContainSingle()
            .Which.ActualHeight.Should().BeGreaterThan(56);
        descendants.OfType<WpfUiAutoSuggestBox>()
            .Count(box => string.Equals(box.PlaceholderText, "Search", StringComparison.Ordinal))
            .Should().BeGreaterThanOrEqualTo(2);

        outerNavigationView.ActualWidth.Should().BeGreaterThan(320);
        var shellPane = descendants.OfType<Border>()
            .Where(border => border.Background is SolidColorBrush brush
                && brush.Color == Color.FromRgb(0x24, 0x18, 0x29))
            .Should().Contain(border =>
                border.IsVisible
                && border.ActualWidth > 360
                && border.ActualHeight > root.ActualHeight * 0.7).Which;
        shellPane.TransformToAncestor(root).Transform(new Point()).X.Should().BeLessThan(4);

        var titleLeft = FindTextBlock(descendants, "NavigationView").TransformToAncestor(root).Transform(new Point()).X;
        titleLeft.Should().BeInRange(440, 480);
    }

    private static TextBlock FindTextBlock(IEnumerable<DependencyObject> descendants, string text)
        => descendants.OfType<TextBlock>()
            .Where(textBlock => string.Equals(textBlock.Text, text, StringComparison.Ordinal))
            .OrderByDescending(textBlock => textBlock.FontSize)
            .First();

    private static void AssertColorTileVisible(IEnumerable<DependencyObject> descendants, Color color)
    {
        var tiles = descendants.OfType<Border>()
            .Where(border => HasSolidBackground(border, color))
            .Select(border => new
            {
                border.IsVisible,
                Width = border.ActualWidth,
                Height = border.ActualHeight
            })
            .ToArray();

        tiles.Should().Contain(tile =>
            tile.IsVisible
            && tile.Width > 80
            && tile.Height > 40);
    }

    private static Border FindSolidBorder(IEnumerable<DependencyObject> descendants, Color color)
        => descendants.OfType<Border>().First(border => HasSolidBackground(border, color));

    private static bool HasSolidBackground(Border border, Color color)
        => border.Background is SolidColorBrush brush && brush.Color == color;

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
