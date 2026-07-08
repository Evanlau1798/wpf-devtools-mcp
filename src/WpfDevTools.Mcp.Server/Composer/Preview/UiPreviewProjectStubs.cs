namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class UiPreviewProjectStubs
{
    public const string WpfUi =
        """
        using System.Collections.ObjectModel;
        using System.Windows;
        using System.Windows.Controls;
        using System.Windows.Markup;

        [assembly: XmlnsDefinition("http://schemas.lepo.co/wpfui/2022/xaml", "Wpf.Ui.Controls")]

        namespace Wpf.Ui.Controls;

        public class Button : ContentControl
        {
            public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
                nameof(Icon), typeof(object), typeof(Button));
            public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
                nameof(Appearance), typeof(string), typeof(Button));
            public object? Icon
            {
                get => GetValue(IconProperty);
                set => SetValue(IconProperty, value);
            }

            public string? Appearance
            {
                get => (string?)GetValue(AppearanceProperty);
                set => SetValue(AppearanceProperty, value);
            }
        }

        public class SymbolIcon : Control
        {
            public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
                nameof(Symbol), typeof(string), typeof(SymbolIcon));
            public string? Symbol
            {
                get => (string?)GetValue(SymbolProperty);
                set => SetValue(SymbolProperty, value);
            }
        }

        public class TextBlock : System.Windows.Controls.TextBlock
        {
            public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
                nameof(Appearance), typeof(string), typeof(TextBlock));
            public string? Appearance
            {
                get => (string?)GetValue(AppearanceProperty);
                set => SetValue(AppearanceProperty, value);
            }
        }

        public class Card : ItemsControl
        {
            public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
                nameof(Appearance), typeof(string), typeof(Card));
            public string? Appearance
            {
                get => (string?)GetValue(AppearanceProperty);
                set => SetValue(AppearanceProperty, value);
            }
        }

        public class NavigationView : Control
        {
            public static readonly DependencyProperty PaneDisplayModeProperty = DependencyProperty.Register(
                nameof(PaneDisplayMode), typeof(string), typeof(NavigationView));
            public static readonly DependencyProperty ContentOverlayProperty = DependencyProperty.Register(
                nameof(ContentOverlay), typeof(object), typeof(NavigationView));
            public Collection<object> MenuItems { get; } = new();
            public Collection<object> FooterMenuItems { get; } = new();
            public object? ContentOverlay
            {
                get => GetValue(ContentOverlayProperty);
                set => SetValue(ContentOverlayProperty, value);
            }

            public string? PaneDisplayMode
            {
                get => (string?)GetValue(PaneDisplayModeProperty);
                set => SetValue(PaneDisplayModeProperty, value);
            }
        }

        public class NavigationViewItem : HeaderedContentControl
        {
            public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
                nameof(Icon), typeof(object), typeof(NavigationViewItem));
            public static readonly DependencyProperty TargetPageTagProperty = DependencyProperty.Register(
                nameof(TargetPageTag), typeof(string), typeof(NavigationViewItem));
            public object? Icon
            {
                get => GetValue(IconProperty);
                set => SetValue(IconProperty, value);
            }

            public string? TargetPageTag
            {
                get => (string?)GetValue(TargetPageTagProperty);
                set => SetValue(TargetPageTagProperty, value);
            }
        }

        public class TabView : ItemsControl
        {
            public int SelectedIndex { get; set; }
        }

        public class TabViewItem : HeaderedContentControl
        {
            public bool IsClosable { get; set; }
        }

        public class ContentDialog : ItemsControl
        {
            public string? Title { get; set; }
        }

        public class Snackbar : ItemsControl
        {
            public double Timeout { get; set; }
        }

        public class TitleBar : Control
        {
            public string? Title { get; set; }
        }

        public class FluentWindow : Window
        {
            public static readonly DependencyProperty TitleBarProperty = DependencyProperty.Register(
                nameof(TitleBar), typeof(object), typeof(FluentWindow));
            public object? TitleBar
            {
                get => GetValue(TitleBarProperty);
                set => SetValue(TitleBarProperty, value);
            }
        }

        public class DataGrid : ItemsControl
        {
            public Collection<object> Columns { get; } = new();
        }
        """;
}
