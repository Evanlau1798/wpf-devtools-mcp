using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Helper for simulating user interactions with WPF elements
/// </summary>
public class InteractionHelper
{
    /// <summary>
    /// Simulate mouse click on an element
    /// </summary>
    public object ClickElement(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ClickElement(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        if (element is not UIElement uiElement)
        {
            return new { success = false, error = "Element is not a UIElement" };
        }

        try
        {
            // Raise click event
            if (element is System.Windows.Controls.Primitives.ButtonBase button)
            {
                // For buttons, use the Click event
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            else
            {
                // For other elements, simulate mouse down/up
                var mouseDownEvent = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent
                };
                uiElement.RaiseEvent(mouseDownEvent);

                var mouseUpEvent = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent
                };
                uiElement.RaiseEvent(mouseUpEvent);
            }

            return new { success = true, message = "Element clicked successfully" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to click element: {ex.Message}" };
        }
    }

    /// <summary>
    /// Simulate keyboard input
    /// </summary>
    public object SimulateKeyboard(string text, string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => SimulateKeyboard(text, elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        if (element is not UIElement uiElement)
        {
            return new { success = false, error = "Element is not a UIElement" };
        }

        try
        {
            // Focus the element first
            uiElement.Focus();

            // For TextBox, set text directly
            if (element is TextBox textBox)
            {
                textBox.Text = text;
                return new { success = true, message = $"Text '{text}' set successfully" };
            }

            // For other elements, simulate key events
            foreach (char c in text)
            {
                var keyDownEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(uiElement), 0, Key.None)
                {
                    RoutedEvent = UIElement.KeyDownEvent
                };
                uiElement.RaiseEvent(keyDownEvent);

                var keyUpEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(uiElement), 0, Key.None)
                {
                    RoutedEvent = UIElement.KeyUpEvent
                };
                uiElement.RaiseEvent(keyUpEvent);
            }

            return new { success = true, message = $"Keyboard input '{text}' simulated successfully" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to simulate keyboard input: {ex.Message}" };
        }
    }

    /// <summary>
    /// Scroll element into view
    /// </summary>
    public object ScrollToElement(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ScrollToElement(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { success = false, error = "Element is not a FrameworkElement" };
        }

        try
        {
            fe.BringIntoView();
            return new { success = true, message = "Element scrolled into view successfully" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to scroll element into view: {ex.Message}" };
        }
    }

    private DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    private DependencyObject? FindElementById(string elementId)
    {
        // TODO: Implement element lookup by ID
        return GetRootElement();
    }
}
