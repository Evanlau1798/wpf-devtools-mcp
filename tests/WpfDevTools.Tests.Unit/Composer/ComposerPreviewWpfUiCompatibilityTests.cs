using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewWpfUiCompatibilityTests
{
    [Fact]
    public void WpfUiPreviewStubs_ShouldExposeNavigationViewChildrenForDiagnostics()
    {
        UiPreviewProjectStubs.WpfUi.Should().Contain("public class NavigationView : StackPanel");
        UiPreviewProjectStubs.WpfUi.Should().Contain("public ObservableCollection<object> MenuItems");
        UiPreviewProjectStubs.WpfUi.Should().Contain("public ObservableCollection<object> FooterMenuItems");
        UiPreviewProjectStubs.WpfUi.Should().Contain("public object? AutoSuggestBox");
        UiPreviewProjectStubs.WpfUi.Should().Contain("StubVisuals.Add(Children, AutoSuggestBox)");
        UiPreviewProjectStubs.WpfUi.Should().Contain("StubVisuals.Add(Children, ContentOverlay)");
        UiPreviewProjectStubs.WpfUi.Should().NotContain("public class NavigationView : ItemsControl");
    }

    [Fact]
    public void WpfUiPreviewStubs_ShouldExposeImageIconForTitleBar()
    {
        UiPreviewProjectStubs.WpfUi.Should().Contain("public class ImageIcon : System.Windows.Controls.Image");
        UiPreviewProjectStubs.WpfUi.Should().Contain("public class TitleBar : Control");
        UiPreviewProjectStubs.WpfUi.Should().Contain("public object? Icon");
    }
}
