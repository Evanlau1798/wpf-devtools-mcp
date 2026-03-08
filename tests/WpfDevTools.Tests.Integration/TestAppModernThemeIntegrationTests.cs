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
    public void ModernThemeSelections_ShouldUpdateDiagnosticsAndExposeNamedGoldenElements()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var window = new MainWindow();

            try
            {
                window.Show();
                window.UpdateLayout();

                var themeSelector = window.FindName("ThemeModeSelector") as ComboBox;
                var accentSelector = window.FindName("AccentSelector") as ComboBox;
                var primaryButton = window.FindName("ModernPrimaryButton") as Button;
                var subtleButton = window.FindName("ModernSubtleButton") as Button;
                var inputTextBox = window.FindName("ModernInputTextBox") as TextBox;
                var roundedToggle = window.FindName("ModernRoundedToggle") as CheckBox;

                themeSelector.Should().NotBeNull();
                accentSelector.Should().NotBeNull();

                themeSelector!.SelectedIndex = 1;
                accentSelector!.SelectedIndex = 2;
                window.UpdateLayout();

                var themeModeText = (window.FindName("CurrentThemeModeText") as TextBlock)?.Text;
                var accentText = (window.FindName("CurrentAccentValueText") as TextBlock)?.Text;
                var accentHexText = (window.FindName("CurrentAccentHexText") as TextBlock)?.Text;

                return (
                    ThemeModeText: themeModeText,
                    AccentText: accentText,
                    AccentHexText: accentHexText,
                    PrimaryButtonFound: primaryButton is not null,
                    SubtleButtonFound: subtleButton is not null,
                    InputTextBoxFound: inputTextBox is not null,
                    RoundedToggleFound: roundedToggle is not null);
            }
            finally
            {
                window.Close();
            }
        });

        result.ThemeModeText.Should().Be("Dark");
        result.AccentText.Should().Be("Emerald");
        result.AccentHexText.Should().Be("#FF059669");
        result.PrimaryButtonFound.Should().BeTrue();
        result.SubtleButtonFound.Should().BeTrue();
        result.InputTextBoxFound.Should().BeTrue();
        result.RoundedToggleFound.Should().BeTrue();
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

    [Fact]
    public void ModernShellWindow_ShouldStayInSyncWithThemeSelections()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var window = new MainWindow();

            try
            {
                window.Show();
                window.UpdateLayout();

                var openButton = window.FindName("OpenModernWindowButton") as Button;
                var themeSelector = window.FindName("ThemeModeSelector") as ComboBox;
                var accentSelector = window.FindName("AccentSelector") as ComboBox;
                openButton.Should().NotBeNull();
                themeSelector.Should().NotBeNull();
                accentSelector.Should().NotBeNull();

                openButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                themeSelector!.SelectedIndex = 1;
                accentSelector!.SelectedIndex = 1;
                window.UpdateLayout();

                var modernShell = Application.Current.Windows
                    .OfType<Window>()
                    .Single(candidate => candidate.GetType().Name == "ModernShellWindow");

                var shellThemeText = (modernShell.FindName("ThemeModeText") as TextBlock)?.Text;
                var shellPrimaryAction = modernShell.FindName("ModernPrimaryActionButton") as Button;

                return (
                    ShellThemeText: shellThemeText,
                    ShellPrimaryActionFound: shellPrimaryAction is not null);
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

        result.ShellThemeText.Should().Be("Dark");
        result.ShellPrimaryActionFound.Should().BeTrue();
    }
}
