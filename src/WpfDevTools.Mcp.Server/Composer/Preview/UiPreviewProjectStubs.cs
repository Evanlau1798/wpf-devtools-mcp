namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class UiPreviewProjectStubs
{
    public const string WpfUi =
        """
        using System.Collections.ObjectModel;
        using System.Collections.Specialized;
        using System.Text;
        using System.Windows;
        using System.Windows.Controls;
        using System.Windows.Markup;
        using System.Windows.Media;

        [assembly: XmlnsDefinition("http://schemas.lepo.co/wpfui/2022/xaml", "Wpf.Ui.Controls")]

        namespace Wpf.Ui.Controls;

        public class Button : System.Windows.Controls.Button
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

            public Button()
            {
                HorizontalAlignment = HorizontalAlignment.Left;
                Padding = new Thickness(14, 8, 14, 8);
                Margin = new Thickness(0, 10, 0, 0);
                Background = StubVisuals.Brush(218, 112, 214);
                Foreground = StubVisuals.Brush(24, 20, 27);
                BorderThickness = new Thickness(0);
            }
        }

        public class SymbolIcon : System.Windows.Controls.TextBlock
        {
            public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
                nameof(Symbol), typeof(string), typeof(SymbolIcon), new PropertyMetadata(null, OnSymbolChanged));
            public string? Symbol
            {
                get => (string?)GetValue(SymbolProperty);
                set => SetValue(SymbolProperty, value);
            }

            private static void OnSymbolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
                => ((SymbolIcon)d).Text = e.NewValue as string;

            public SymbolIcon()
            {
                Foreground = StubVisuals.Brush(232, 228, 235);
                Margin = new Thickness(0, 0, 8, 0);
            }
        }

        public class ImageIcon : System.Windows.Controls.Image
        {
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

            public TextBlock()
            {
                Foreground = StubVisuals.Brush(232, 228, 235);
                TextWrapping = TextWrapping.Wrap;
                Margin = new Thickness(0, 2, 0, 2);
            }
        }

        public class AutoSuggestBox : System.Windows.Controls.TextBox
        {
            public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
                nameof(PlaceholderText), typeof(string), typeof(AutoSuggestBox), new PropertyMetadata(null, OnPlaceholderTextChanged));
            public string? PlaceholderText
            {
                get => (string?)GetValue(PlaceholderTextProperty);
                set => SetValue(PlaceholderTextProperty, value);
            }

            public AutoSuggestBox()
            {
                Height = 36;
                Margin = new Thickness(0, 0, 0, 10);
                Padding = new Thickness(10, 6, 10, 6);
                Background = StubVisuals.Brush(53, 42, 58);
                Foreground = StubVisuals.Brush(232, 228, 235);
                BorderBrush = StubVisuals.Brush(84, 69, 91);
            }

            private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                var textBox = (AutoSuggestBox)d;
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = e.NewValue as string;
                }
            }
        }

        public class Card : StackPanel
        {
            public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
                nameof(Footer), typeof(object), typeof(Card));
            public object? Footer
            {
                get => GetValue(FooterProperty);
                set => SetValue(FooterProperty, value);
            }

            public Card()
            {
                Margin = new Thickness(16);
                Background = StubVisuals.Brush(48, 56, 65);
                Loaded += (_, _) => StubVisuals.Add(Children, Footer);
            }
        }

        public class NavigationView : StackPanel
        {
            public static readonly DependencyProperty PaneDisplayModeProperty = DependencyProperty.Register(
                nameof(PaneDisplayMode), typeof(string), typeof(NavigationView));
            public static readonly DependencyProperty IsBackButtonVisibleProperty = DependencyProperty.Register(
                nameof(IsBackButtonVisible), typeof(string), typeof(NavigationView));
            public static readonly DependencyProperty IsPaneToggleVisibleProperty = DependencyProperty.Register(
                nameof(IsPaneToggleVisible), typeof(bool), typeof(NavigationView));
            public static readonly DependencyProperty IsTopSeparatorVisibleProperty = DependencyProperty.Register(
                nameof(IsTopSeparatorVisible), typeof(bool), typeof(NavigationView));
            public static readonly DependencyProperty IsFooterSeparatorVisibleProperty = DependencyProperty.Register(
                nameof(IsFooterSeparatorVisible), typeof(bool), typeof(NavigationView));
            public static readonly DependencyProperty OpenPaneLengthProperty = DependencyProperty.Register(
                nameof(OpenPaneLength), typeof(double), typeof(NavigationView));
            public static readonly DependencyProperty AutoSuggestBoxProperty = DependencyProperty.Register(
                nameof(AutoSuggestBox), typeof(object), typeof(NavigationView), new PropertyMetadata(null, OnAutoSuggestBoxChanged));
            public static readonly DependencyProperty ContentOverlayProperty = DependencyProperty.Register(
                nameof(ContentOverlay), typeof(object), typeof(NavigationView), new PropertyMetadata(null, OnContentOverlayChanged));
            public ObservableCollection<object> MenuItems { get; } = new();
            public ObservableCollection<object> FooterMenuItems { get; } = new();
            public object? ContentOverlay
            {
                get => GetValue(ContentOverlayProperty);
                set => SetValue(ContentOverlayProperty, value);
            }

            public object? AutoSuggestBox
            {
                get => GetValue(AutoSuggestBoxProperty);
                set => SetValue(AutoSuggestBoxProperty, value);
            }

            public string? PaneDisplayMode
            {
                get => (string?)GetValue(PaneDisplayModeProperty);
                set => SetValue(PaneDisplayModeProperty, value);
            }

            public string? IsBackButtonVisible
            {
                get => (string?)GetValue(IsBackButtonVisibleProperty);
                set => SetValue(IsBackButtonVisibleProperty, value);
            }

            public bool IsPaneToggleVisible
            {
                get => (bool)GetValue(IsPaneToggleVisibleProperty);
                set => SetValue(IsPaneToggleVisibleProperty, value);
            }

            public bool IsTopSeparatorVisible
            {
                get => (bool)GetValue(IsTopSeparatorVisibleProperty);
                set => SetValue(IsTopSeparatorVisibleProperty, value);
            }

            public bool IsFooterSeparatorVisible
            {
                get => (bool)GetValue(IsFooterSeparatorVisibleProperty);
                set => SetValue(IsFooterSeparatorVisibleProperty, value);
            }

            public double OpenPaneLength
            {
                get => (double)GetValue(OpenPaneLengthProperty);
                set => SetValue(OpenPaneLengthProperty, value);
            }

            public NavigationView()
            {
                Orientation = Orientation.Vertical;
                Background = Brushes.Transparent;
                MenuItems.CollectionChanged += OnCollectionChanged;
                FooterMenuItems.CollectionChanged += OnCollectionChanged;
            }

            private static void OnContentOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
                => ((NavigationView)d).Rebuild();

            private static void OnAutoSuggestBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
                => ((NavigationView)d).Rebuild();

            private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
                => Rebuild();

            private void Rebuild()
            {
                Children.Clear();
                StubVisuals.Add(Children, AutoSuggestBox);
                var pane = new StackPanel { MinWidth = 160 };
                foreach (var item in MenuItems)
                {
                    StubVisuals.Add(pane.Children, item);
                }

                foreach (var item in FooterMenuItems)
                {
                    StubVisuals.Add(pane.Children, item);
                }

                if (pane.Children.Count > 0)
                {
                    Children.Add(pane);
                }

                StubVisuals.Add(Children, ContentOverlay);
            }
        }

        public class NavigationViewItem : System.Windows.Controls.Button
        {
            public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
                nameof(Icon), typeof(object), typeof(NavigationViewItem));
            public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
                nameof(IsActive), typeof(bool), typeof(NavigationViewItem), new PropertyMetadata(false, OnIsActiveChanged));
            public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
                nameof(IsExpanded), typeof(bool), typeof(NavigationViewItem));
            public static readonly DependencyProperty TargetPageTagProperty = DependencyProperty.Register(
                nameof(TargetPageTag), typeof(string), typeof(NavigationViewItem));
            public object? Icon
            {
                get => GetValue(IconProperty);
                set => SetValue(IconProperty, value);
            }

            public bool IsActive
            {
                get => (bool)GetValue(IsActiveProperty);
                set => SetValue(IsActiveProperty, value);
            }

            public bool IsExpanded
            {
                get => (bool)GetValue(IsExpandedProperty);
                set => SetValue(IsExpandedProperty, value);
            }

            public Collection<object> MenuItems { get; } = new();

            public string? TargetPageTag
            {
                get => (string?)GetValue(TargetPageTagProperty);
                set => SetValue(TargetPageTagProperty, value);
            }

            public NavigationViewItem()
            {
                HorizontalContentAlignment = HorizontalAlignment.Left;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                Padding = new Thickness(12, 8, 12, 8);
                Margin = new Thickness(0, 2, 0, 2);
                Foreground = StubVisuals.Brush(232, 228, 235);
                Background = Brushes.Transparent;
                BorderThickness = new Thickness(0);
            }

            private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
                => ((NavigationViewItem)d).Background = (bool)e.NewValue
                    ? StubVisuals.Brush(57, 44, 62)
                    : Brushes.Transparent;
        }

        public class NavigationViewItemSeparator : Separator;

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

        public class TitleBar : Border
        {
            public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
                nameof(Icon), typeof(object), typeof(TitleBar), new PropertyMetadata(null, OnVisualChanged));
            public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
                nameof(Title), typeof(string), typeof(TitleBar), new PropertyMetadata(null, OnVisualChanged));
            public object? Icon
            {
                get => GetValue(IconProperty);
                set => SetValue(IconProperty, value);
            }

            public string? Title
            {
                get => (string?)GetValue(TitleProperty);
                set => SetValue(TitleProperty, value);
            }

            public TitleBar()
            {
                Background = StubVisuals.Brush(29, 29, 29);
                Padding = new Thickness(24, 0, 24, 0);
            }

            private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
                => ((TitleBar)d).Rebuild();

            private void Rebuild()
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                StubVisuals.Add(content.Children, Icon);
                content.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = Title,
                    Foreground = StubVisuals.Brush(242, 242, 242),
                    VerticalAlignment = VerticalAlignment.Center
                });
                Child = content;
            }
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

            public FluentWindow()
            {
                Background = StubVisuals.Brush(37, 43, 51);
                Foreground = StubVisuals.Brush(232, 228, 235);
            }
        }

        public class DataGrid : ItemsControl
        {
            public Collection<object> Columns { get; } = new();
        }

        internal static class StubVisuals
        {
            public static SolidColorBrush Brush(byte red, byte green, byte blue)
                => new(Color.FromRgb(red, green, blue));

            public static void Add(UIElementCollection children, object? value)
            {
                if (value is UIElement element)
                {
                    try
                    {
                        children.Add(element);
                    }
                    catch (InvalidOperationException) when (LogicalTreeHelper.GetParent(element) is Panel parent)
                    {
                        parent.Children.Remove(element);
                        children.Add(element);
                    }
                    catch (InvalidOperationException)
                    {
                        AddText(children, value);
                    }
                }
                else if (value is not null)
                {
                    AddText(children, value);
                }
            }

            private static void AddText(UIElementCollection children, object value)
            {
                var text = Describe(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    children.Add(new System.Windows.Controls.TextBlock { Text = text });
                }
            }

            private static string Describe(object? value)
            {
                var builder = new StringBuilder();
                AppendText(builder, value);
                return builder.ToString().Trim();
            }

            private static void AppendText(StringBuilder builder, object? value)
            {
                switch (value)
                {
                    case null:
                        return;
                    case string text:
                        AppendWord(builder, text);
                        return;
                    case System.Windows.Controls.TextBlock textBlock:
                        AppendWord(builder, textBlock.Text);
                        return;
                    case HeaderedContentControl headered:
                        AppendText(builder, headered.Header);
                        AppendText(builder, headered.Content);
                        return;
                    case ContentControl content:
                        AppendText(builder, content.Content);
                        return;
                    case Panel panel:
                        foreach (UIElement child in panel.Children)
                        {
                            AppendText(builder, child);
                        }
                        return;
                    default:
                        AppendWord(builder, value.ToString());
                        return;
                }
            }

            private static void AppendWord(StringBuilder builder, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(value);
            }
        }
        """;
}
