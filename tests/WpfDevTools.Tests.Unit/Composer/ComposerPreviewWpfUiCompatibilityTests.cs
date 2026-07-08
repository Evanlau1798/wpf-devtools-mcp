using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewWpfUiCompatibilityTests
{
    [Fact]
    public void WpfUiPreviewStubs_ShouldModelNavigationViewContentContract()
    {
        UiPreviewProjectStubs.WpfUi.Should().Contain("public class NavigationView : Control");
        UiPreviewProjectStubs.WpfUi.Should().Contain("public object? ContentOverlay");
        UiPreviewProjectStubs.WpfUi.Should().NotContain("public class NavigationView : ItemsControl");
    }
}
