using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfDevTools.Inspector.Utilities;

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
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not UIElement uiElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a UIElement",
                    "Choose a UIElement target from get_visual_tree before simulating keyboard input.");
            }

            try
            {
                if (!Enum.TryParse<Key>(key, out var parsedKey))
                {
                    return ToolErrorFactory.InvalidArgument(
                        $"Invalid key: {key}",
                        "Use a valid WPF Key enum name such as Enter, Tab, Space, or Escape.");
                }

                var routedEvent = ParseKeyboardEvent(eventType);
                if (routedEvent == null)
                {
                    return ToolErrorFactory.InvalidArgument(
                        "Invalid event type. Use 'KeyDown' or 'KeyUp'",
                        "Set eventType to 'KeyDown' or 'KeyUp' when calling simulate_keyboard.");
                }

                var readinessError = GetKeyboardInteractionError(uiElement, requireFocusable: false);
                if (readinessError != null)
                {
                    return readinessError;
                }

                var focusedElementIdBefore = GetFocusedElementId();
                var presentationSource = PresentationSource.FromVisual(uiElement);
                if (presentationSource == null)
                {
                    return CreateDetachedVisualTreeError();
                }

                if (routedEvent == Keyboard.KeyDownEvent &&
                    element is TextBox textBox &&
                    InteractionKeyboardHelper.TryApplyTextBoxEdit(textBox, parsedKey))
                {
                    var focusedElementIdAfter = GetFocusedElementId();
                    var focusChanged = !string.Equals(
                        focusedElementIdBefore,
                        focusedElementIdAfter,
                        StringComparison.Ordinal);
                    return CreateKeyboardResult(
                        element,
                        key,
                        eventType,
                        appliedDirectEdit: true,
                        focusChanged: focusChanged,
                        semanticEffectObserved: true,
                        focusedElementIdBefore: focusedElementIdBefore,
                        focusedElementIdAfter: focusedElementIdAfter);
                }

                EnsureElementFocused(uiElement);

                if (routedEvent == Keyboard.KeyDownEvent &&
                    InteractionKeyboardHelper.TryApplySpecialControlAction(uiElement, parsedKey))
                {
                    var focusedElementIdAfter = GetFocusedElementId();
                    var focusChanged = !string.Equals(
                        focusedElementIdBefore,
                        focusedElementIdAfter,
                        StringComparison.Ordinal);
                    return CreateKeyboardResult(element, key, eventType,
                        appliedDirectEdit: true,
                        focusChanged: focusChanged,
                        semanticEffectObserved: true,
                        focusedElementIdBefore: focusedElementIdBefore,
                        focusedElementIdAfter: focusedElementIdAfter);
                }

                InteractionKeyboardHelper.RaisePreviewEvent(uiElement, presentationSource, parsedKey, routedEvent);
                uiElement.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, 0, parsedKey)
                {
                    RoutedEvent = routedEvent
                });

                if (routedEvent == Keyboard.KeyDownEvent && parsedKey == Key.Tab)
                {
                    InteractionKeyboardHelper.TryMoveFocus(uiElement, parsedKey);
                    var focusedElementIdAfter = GetFocusedElementId();
                    var focusChanged = !string.Equals(
                        focusedElementIdBefore,
                        focusedElementIdAfter,
                        StringComparison.Ordinal);
                    return CreateKeyboardResult(
                        element,
                        key,
                        eventType,
                        focusChanged: focusChanged,
                        semanticEffectObserved: focusChanged,
                        focusedElementIdBefore: focusedElementIdBefore,
                        focusedElementIdAfter: focusedElementIdAfter);
                }

                var focusedElementIdAfterDefault = GetFocusedElementId();
                var focusChangedDefault = !string.Equals(
                    focusedElementIdBefore,
                    focusedElementIdAfterDefault,
                    StringComparison.Ordinal);
                return CreateKeyboardResult(
                    element,
                    key,
                    eventType,
                    focusChanged: focusChangedDefault,
                    semanticEffectObserved: focusChangedDefault,
                    focusedElementIdBefore: focusedElementIdBefore,
                    focusedElementIdAfter: focusedElementIdAfterDefault);
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "simulate keyboard input",
                    ex,
                    "Ensure the target can receive focus and is still attached to the current visual tree before retrying.");
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
