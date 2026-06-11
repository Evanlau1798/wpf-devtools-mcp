using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfDevTools.Tests.TestApp;

public partial class MainWindow
{
    private readonly ModernThemeState _modernThemeState =
        new(ModernBackdropCapabilities.Evaluate(Environment.OSVersion.Version));

    private ModernShellWindow? _modernShellWindow;

    private void InitializeModernTheme()
    {
        ThemeModeSelector.SelectedIndex = 2;
        AccentSelector.SelectedIndex = 0;
        ApplyModernThemeState();
    }

    private void ThemeModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _modernThemeState.ApplyThemeMode(ThemeModeSelector.SelectedIndex switch
        {
            0 => ModernThemeMode.Light,
            1 => ModernThemeMode.Dark,
            _ => ModernThemeMode.System
        });

        ApplyModernThemeState();
    }

    private void AccentSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _modernThemeState.ApplyAccent(AccentSelector.SelectedIndex switch
        {
            1 => ModernAccentPreset.Purple,
            2 => ModernAccentPreset.Emerald,
            _ => ModernAccentPreset.Blue
        });

        ApplyModernThemeState();
    }

    private void OpenModernWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modernShellWindow is null || !_modernShellWindow.IsLoaded)
        {
            _modernShellWindow = new ModernShellWindow { Owner = this };
            _modernShellWindow.Closed += (_, _) => _modernShellWindow = null;
            _modernShellWindow.ApplyState(_modernThemeState);
            _modernShellWindow.Show();
            return;
        }

        _modernShellWindow.ApplyState(_modernThemeState);
        _modernShellWindow.Activate();
    }

    private void ApplyModernThemeState()
    {
        Resources["ModernSurfaceBrush"] = CreateBrush(_modernThemeState.SurfaceHex);
        Resources["ModernCardBrush"] = CreateBrush(_modernThemeState.CardHex);
        Resources["ModernAccentBrush"] = CreateBrush(_modernThemeState.AccentHex);
        Resources["ModernForegroundBrush"] = CreateBrush(_modernThemeState.ForegroundHex);
        Resources["ModernMutedForegroundBrush"] = CreateBrush(_modernThemeState.MutedForegroundHex);
        Resources["ModernCornerRadius"] = new CornerRadius(_modernThemeState.CornerRadius);

        CurrentThemeModeText.Text = _modernThemeState.ThemeModeText;
        CurrentAccentValueText.Text = _modernThemeState.AccentText;
        CurrentAccentHexText.Text = _modernThemeState.AccentHex;
        CurrentCornerRadiusText.Text = _modernThemeState.CornerRadiusText;
        BackdropSupportText.Text = _modernThemeState.BackdropSupportedText;

        _modernShellWindow?.ApplyState(_modernThemeState);
    }

    private static SolidColorBrush CreateBrush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex)!);
}
