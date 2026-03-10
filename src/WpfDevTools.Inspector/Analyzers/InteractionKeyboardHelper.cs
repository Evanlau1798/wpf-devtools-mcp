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

    internal static bool TryMoveFocus(UIElement uiElement, Key key)
    {
        if (key != Key.Tab)
        {
            return false;
        }

        EnsureKeyboardFocus(uiElement);
        var request = new TraversalRequest(FocusNavigationDirection.Next);
        var moved = uiElement.MoveFocus(request);
        return moved && !ReferenceEquals(Keyboard.FocusedElement, uiElement);
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
    /// Attempts to apply direct text editing for TextBox.
    /// Handles character insertion (A-Z, 0-9, Space, punctuation) and
    /// deletion (Backspace, Delete). Returns true if the edit was applied.
    /// </summary>
    internal static bool TryApplyTextBoxEdit(TextBox textBox, Key key)
    {
        if (textBox.IsReadOnly)
        {
            return false;
        }

        var ch = TryGetCharForKey(key);
        var isDeleteKey = key == Key.Back || key == Key.Delete;

        if (ch == null && !isDeleteKey)
        {
            return false;
        }

        EnsureFocusAndCaret(textBox);
        var currentText = textBox.Text ?? string.Empty;

        // Handle selected text replacement/deletion
        if (textBox.SelectionLength > 0)
        {
            var selStart = ClampInt(textBox.SelectionStart, 0, currentText.Length);
            var selLen = Math.Min(textBox.SelectionLength, currentText.Length - selStart);
            var afterRemoval = currentText.Remove(selStart, selLen);

            if (ch != null)
            {
                textBox.Text = afterRemoval.Insert(selStart, ch.Value.ToString());
                textBox.CaretIndex = selStart + 1;
            }
            else
            {
                textBox.Text = afterRemoval;
                textBox.CaretIndex = selStart;
            }

            return true;
        }

        // Character insertion at caret
        if (ch != null)
        {
            var caretIndex = ClampInt(textBox.CaretIndex, 0, currentText.Length);
            textBox.Text = currentText.Insert(caretIndex, ch.Value.ToString());
            textBox.CaretIndex = caretIndex + 1;
            return true;
        }

        // Backspace / Delete
        return key == Key.Back
            ? TryApplyBackspace(textBox, currentText)
            : TryApplyDelete(textBox, currentText);
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

    private static void EnsureFocusAndCaret(TextBox textBox)
    {
        var hadKeyboardFocus = textBox.IsKeyboardFocusWithin;
        if (!hadKeyboardFocus)
        {
            EnsureKeyboardFocus(textBox);
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
    }

    /// <summary>
    /// Converts a WPF Key to its printable character representation.
    /// Returns null for non-printable keys (Ctrl, Shift, F1, etc.).
    /// </summary>
    internal static char? TryGetCharForKey(Key key)
    {
        // Letters: A-Z → 'a'-'z'
        if (key >= Key.A && key <= Key.Z)
        {
            return (char)('a' + (key - Key.A));
        }

        // Digits: D0-D9 → '0'-'9'
        if (key >= Key.D0 && key <= Key.D9)
        {
            return (char)('0' + (key - Key.D0));
        }

        // NumPad digits: NumPad0-NumPad9 → '0'-'9'
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return (char)('0' + (key - Key.NumPad0));
        }

        // Common punctuation and symbols
        return key switch
        {
            Key.Space => ' ',
            Key.OemPeriod => '.',
            Key.OemComma => ',',
            Key.OemMinus => '-',
            Key.OemPlus => '=',
            Key.Oem1 => ';',        // Semicolon
            Key.Oem2 => '/',        // Forward slash
            Key.Oem3 => '`',        // Backtick
            Key.Oem4 => '[',        // Open bracket
            Key.Oem5 => '\\',       // Backslash
            Key.Oem6 => ']',        // Close bracket
            Key.Oem7 => '\'',       // Single quote
            Key.Multiply => '*',
            Key.Add => '+',
            Key.Subtract => '-',
            Key.Decimal => '.',
            Key.Divide => '/',
            _ => null
        };
    }

    private static void EnsureKeyboardFocus(UIElement uiElement)
    {
        uiElement.Focus();
        Keyboard.Focus(uiElement);
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
