using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfDevTools.Tests.TestApp;

/// <summary>
/// Custom control with DependencyProperty for testing
/// </summary>
public class CustomTextBox : TextBox
{
    // Custom DependencyProperty
    public static readonly DependencyProperty WatermarkProperty =
        DependencyProperty.Register(
            nameof(Watermark),
            typeof(string),
            typeof(CustomTextBox),
            new PropertyMetadata("Enter text...", OnWatermarkChanged, CoerceWatermark));

    public string Watermark
    {
        get => (string)GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    private static void OnWatermarkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Property changed callback for testing
        if (d is CustomTextBox textBox)
        {
            textBox.ToolTip = $"Watermark: {e.NewValue}";
        }
    }

    private static object CoerceWatermark(DependencyObject d, object baseValue)
    {
        // Coercion callback for testing
        if (baseValue is string str && string.IsNullOrWhiteSpace(str))
        {
            return "Default watermark";
        }
        return baseValue;
    }

    // Attached Property
    public static readonly DependencyProperty HighlightColorProperty =
        DependencyProperty.RegisterAttached(
            "HighlightColor",
            typeof(string),
            typeof(CustomTextBox),
            new PropertyMetadata("Yellow", OnHighlightColorChanged));

    public static string GetHighlightColor(DependencyObject obj)
        => (string)obj.GetValue(HighlightColorProperty);

    public static void SetHighlightColor(DependencyObject obj, string value)
        => obj.SetValue(HighlightColorProperty, value);

    private static void OnHighlightColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control)
        {
            return;
        }

        var value = e.NewValue?.ToString() ?? "Yellow";
        control.ToolTip = $"HighlightColor: {value}";

        if (new BrushConverter().ConvertFromString(value) is Brush brush)
        {
            control.Background = brush;
        }
    }
}

/// <summary>
/// Custom control with RoutedEvent for testing
/// </summary>
public class CustomButton : Button
{
    // Custom RoutedEvent
    public static readonly RoutedEvent CustomClickEvent =
        EventManager.RegisterRoutedEvent(
            nameof(CustomClick),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(CustomButton));

    public event RoutedEventHandler CustomClick
    {
        add => AddHandler(CustomClickEvent, value);
        remove => RemoveHandler(CustomClickEvent, value);
    }

    protected override void OnClick()
    {
        base.OnClick();

        // Raise custom routed event
        RaiseEvent(new RoutedEventArgs(CustomClickEvent, this));
    }
}
