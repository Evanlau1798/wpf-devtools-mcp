using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfDevTools.Tests.TestApp;

public partial class ModernShellWindow : Window
{
    public ModernShellWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyBackdrop();
    }

    public void ApplyState(ModernThemeState state)
    {
        Resources["ModernSurfaceBrush"] = CreateBrush(state.SurfaceHex);
        Resources["ModernCardBrush"] = CreateBrush(state.CardHex);
        Resources["ModernAccentBrush"] = CreateBrush(state.AccentHex);
        Resources["ModernForegroundBrush"] = CreateBrush(state.ForegroundHex);
        Resources["ModernMutedForegroundBrush"] = CreateBrush(state.MutedForegroundHex);

        ThemeModeText.Text = state.ThemeModeText;
        BackdropSupportedText.Text = state.BackdropSupportedText;
        BackdropModeText.Text = state.BackdropModeText;
    }

    private void ApplyBackdrop()
    {
        if (BackdropModeText.Text == "Mica" && !WindowBackdropHelper.TryApplyMica(this))
        {
            BackdropModeText.Text = "Fallback";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static SolidColorBrush CreateBrush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex)!);
}
