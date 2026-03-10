using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class InteractionAnalyzer
{
    /// <summary>
    /// Simulate keyboard input to element
    /// </summary>
    public object SimulateKeyboard(string? elementId, string key, string eventType)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

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
                if (!Enum.TryParse<Key>(key, out var parsedKey))
                {
                    return new { success = false, error = $"Invalid key: {key}" };
                }

                var routedEvent = ParseKeyboardEvent(eventType);
                if (routedEvent == null)
                {
                    return new { success = false, error = "Invalid event type. Use 'KeyDown' or 'KeyUp'" };
                }

                var presentationSource = PresentationSource.FromVisual(uiElement);
                if (presentationSource == null)
                {
                    return new
                    {
                        success = false,
                        error = "Element is not connected to a presentation source",
                        hint = "Element may not be in the visual tree or may not be rendered yet"
                    };
                }

                if (routedEvent == Keyboard.KeyDownEvent &&
                    element is TextBox textBox &&
                    InteractionKeyboardHelper.TryApplyTextBoxEdit(textBox, parsedKey))
                {
                    return CreateKeyboardResult(element, key, eventType, appliedDirectEdit: true);
                }

                if (routedEvent == Keyboard.KeyDownEvent &&
                    InteractionKeyboardHelper.TryApplySpecialControlAction(uiElement, parsedKey))
                {
                    return CreateKeyboardResult(element, key, eventType, appliedDirectEdit: true);
                }

                EnsureElementFocused(uiElement);
                var focusedElementIdBefore = GetFocusedElementId();

                InteractionKeyboardHelper.RaisePreviewEvent(uiElement, presentationSource, parsedKey, routedEvent);
                uiElement.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, 0, parsedKey)
                {
                    RoutedEvent = routedEvent
                });

                if (routedEvent == Keyboard.KeyDownEvent && parsedKey == Key.Tab)
                {
                    var focusChanged = InteractionKeyboardHelper.TryMoveFocus(uiElement, parsedKey);
                    return CreateKeyboardResult(
                        element,
                        key,
                        eventType,
                        focusChanged: focusChanged,
                        semanticEffectObserved: focusChanged,
                        focusedElementIdBefore: focusedElementIdBefore,
                        focusedElementIdAfter: GetFocusedElementId());
                }

                return CreateKeyboardResult(element, key, eventType);
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Failed to simulate keyboard: {ex.Message}" };
            }
        });
    }

    private static RoutedEvent? ParseKeyboardEvent(string eventType)
    {
        if (string.Equals(eventType, "keydown", StringComparison.OrdinalIgnoreCase))
        {
            return Keyboard.KeyDownEvent;
        }

        if (string.Equals(eventType, "keyup", StringComparison.OrdinalIgnoreCase))
        {
            return Keyboard.KeyUpEvent;
        }

        return null;
    }

    private object CreateKeyboardResult(
        object element,
        string key,
        string eventType,
        bool appliedDirectEdit = false,
        bool focusChanged = false,
        bool semanticEffectObserved = false,
        string? focusedElementIdBefore = null,
        string? focusedElementIdAfter = null)
    {
        return new
        {
            success = true,
            message = $"Keyboard event '{eventType}' simulated for key '{key}'",
            key,
            eventType,
            elementType = element.GetType().Name,
            appliedDirectEdit,
            focusChanged,
            semanticEffectObserved,
            focusedElementIdBefore,
            focusedElementIdAfter
        };
    }

    private string? GetFocusedElementId()
    {
        return Keyboard.FocusedElement is DependencyObject dependencyObject
            ? _elementFinder.GenerateElementId(dependencyObject)
            : null;
    }

    private static void EnsureElementFocused(UIElement uiElement)
    {
        if (!uiElement.IsKeyboardFocusWithin)
        {
            uiElement.Focus();
            Keyboard.Focus(uiElement);
        }
    }
}
