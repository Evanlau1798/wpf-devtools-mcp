using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Tests.TestApp;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public sealed class TestAppModernThemeIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public TestAppModernThemeIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void MainWindow_ShouldExposeModernThemeDiagnostics()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var window = new MainWindow();

            try
            {
                window.Show();
                window.UpdateLayout();

                var modernThemeTab = window.FindName("ModernThemeTab") as TabItem;
                var themeModeText = window.FindName("CurrentThemeModeText") as TextBlock;
                var accentText = window.FindName("CurrentAccentValueText") as TextBlock;
                var cornerRadiusText = window.FindName("CurrentCornerRadiusText") as TextBlock;
                var openButton = window.FindName("OpenModernWindowButton") as Button;

                return (
                    TabFound: modernThemeTab is not null,
                    ThemeText: themeModeText?.Text,
                    AccentText: accentText?.Text,
                    CornerRadiusText: cornerRadiusText?.Text,
                    OpenButtonFound: openButton is not null);
            }
            finally
            {
                window.Close();
            }
        });

        result.TabFound.Should().BeTrue();
        result.ThemeText.Should().NotBeNullOrWhiteSpace();
        result.AccentText.Should().NotBeNullOrWhiteSpace();
        result.CornerRadiusText.Should().Be("18");
        result.OpenButtonFound.Should().BeTrue();
    }

    [Fact]
    public void OpenModernWindowButton_ShouldLaunchModernShellWindow()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var window = new MainWindow();

            try
            {
                window.Show();
                window.UpdateLayout();

                var openButton = window.FindName("OpenModernWindowButton") as Button;
                openButton.Should().NotBeNull();

                openButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();

                var modernShell = Application.Current.Windows
                    .OfType<Window>()
                    .SingleOrDefault(candidate => candidate.GetType().Name == "ModernShellWindow");

                var backdropModeText = modernShell?.FindName("BackdropModeText") as TextBlock;
                var backdropSupportedText = modernShell?.FindName("BackdropSupportedText") as TextBlock;
                var themeModeText = modernShell?.FindName("ThemeModeText") as TextBlock;

                return (
                    WindowFound: modernShell is not null,
                    BackdropMode: backdropModeText?.Text,
                    BackdropSupported: backdropSupportedText?.Text,
                    ThemeMode: themeModeText?.Text,
                    ShellWindow: modernShell);
            }
            finally
            {
                foreach (var extraWindow in Application.Current.Windows.OfType<Window>().Where(candidate => candidate != window).ToList())
                {
                    extraWindow.Close();
                }

                window.Close();
            }
        });

        result.WindowFound.Should().BeTrue();
        result.BackdropMode.Should().NotBeNullOrWhiteSpace();
        result.BackdropSupported.Should().NotBeNullOrWhiteSpace();
        result.ThemeMode.Should().NotBeNullOrWhiteSpace();
    }
}
