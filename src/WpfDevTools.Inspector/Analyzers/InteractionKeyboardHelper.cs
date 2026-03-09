using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Helper methods for keyboard simulation on specific WPF controls.
/// Handles special control actions (CheckBox toggle, ComboBox navigation)
/// and preview/bubble event raising.
/// </summary>
internal static class InteractionKeyboardHelper
{
    /// <summary>
    /// Attempts to apply a special keyboard action for controls that need
    /// direct property manipulation rather than just event raising.
    /// </summary>
    /// <returns>True if the action was handled, false otherwise.</returns>
    internal static bool TryApplySpecialControlAction(UIElement uiElement, Key key)
    {
        if (uiElement is CheckBox checkBox && (key == Key.Space || key == Key.Enter))
        {
            checkBox.IsChecked = checkBox.IsChecked != true;
            checkBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, checkBox));
            return true;
        }

        if (uiElement is ComboBox comboBox && comboBox.Items.Count > 0)
        {
            if (key == Key.Down && comboBox.SelectedIndex < comboBox.Items.Count - 1)
            {
                comboBox.SelectedIndex++;
                return true;
            }

            if (key == Key.Up && comboBox.SelectedIndex > 0)
            {
                comboBox.SelectedIndex--;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Raises the preview (tunnel) event before the bubble event for proper
    /// WPF event sequence: PreviewKeyDown -> KeyDown, PreviewKeyUp -> KeyUp.
    /// </summary>
    internal static void RaisePreviewEvent(
        UIElement uiElement,
        PresentationSource presentationSource,
        Key key,
        RoutedEvent bubbleEvent)
    {
        var previewEvent = GetPreviewEvent(bubbleEvent);
        if (previewEvent == null) return;

        var previewArgs = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            presentationSource,
            0,
            key)
        {
            RoutedEvent = previewEvent
        };

        uiElement.RaiseEvent(previewArgs);
    }

    /// <summary>
    /// Attempts to apply direct text editing for TextBox (Backspace, Delete).
    /// Returns true if the edit was applied.
    /// </summary>
    internal static bool TryApplyTextBoxEdit(TextBox textBox, Key key)
    {
        if (textBox.IsReadOnly || (key != Key.Back && key != Key.Delete))
        {
            return false;
        }

        var hadKeyboardFocus = textBox.IsKeyboardFocusWithin;
        if (!hadKeyboardFocus)
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
        }

        var text = textBox.Text ?? string.Empty;
        if (!hadKeyboardFocus && textBox.SelectionLength == 0)
        {
            textBox.CaretIndex = text.Length;
        }
        else
        {
            textBox.CaretIndex = ClampInt(textBox.CaretIndex, 0, text.Length);
        }

        if (textBox.SelectionLength > 0)
        {
            var selectionStart = ClampInt(textBox.SelectionStart, 0, text.Length);
            var selectionLength = Math.Min(textBox.SelectionLength, text.Length - selectionStart);
            textBox.Text = text.Remove(selectionStart, selectionLength);
            textBox.CaretIndex = selectionStart;
            return true;
        }

        return key == Key.Back ? TryApplyBackspace(textBox, text) : TryApplyDelete(textBox, text);
    }

    private static bool TryApplyBackspace(TextBox textBox, string text)
    {
        var caretIndex = ClampInt(textBox.CaretIndex, 0, text.Length);
        if (caretIndex == 0) return false;

        textBox.Text = text.Remove(caretIndex - 1, 1);
        textBox.CaretIndex = caretIndex - 1;
        return true;
    }

    private static bool TryApplyDelete(TextBox textBox, string text)
    {
        var caretIndex = ClampInt(textBox.CaretIndex, 0, text.Length);
        if (caretIndex >= text.Length) return false;

        textBox.Text = text.Remove(caretIndex, 1);
        textBox.CaretIndex = caretIndex;
        return true;
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private static RoutedEvent? GetPreviewEvent(RoutedEvent bubbleEvent)
    {
        if (bubbleEvent == Keyboard.KeyDownEvent) return Keyboard.PreviewKeyDownEvent;
        if (bubbleEvent == Keyboard.KeyUpEvent) return Keyboard.PreviewKeyUpEvent;
        return null;
    }
}
