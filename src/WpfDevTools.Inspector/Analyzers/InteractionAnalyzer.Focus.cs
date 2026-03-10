using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class InteractionAnalyzer
{
    /// <summary>
    /// Get current logical or keyboard focus metadata.
    /// </summary>
    public object GetFocusState(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var scopedElement = elementId == null ? null : _elementFinder.FindById(elementId);
            var window = ResolveFocusWindow(scopedElement);
            var logicalFocus = window != null ? FocusManager.GetFocusedElement(window) : null;
            var keyboardFocus = Keyboard.FocusedElement;
            var focusedElement = logicalFocus as DependencyObject ?? keyboardFocus as DependencyObject;

            return new
            {
                success = true,
                focusKind = logicalFocus != null ? "Logical" : keyboardFocus != null ? "Keyboard" : "None",
                focusedElementId = focusedElement != null ? _elementFinder.GenerateElementId(focusedElement) : null,
                focusedElementType = focusedElement?.GetType().Name,
                windowElementId = window != null ? _elementFinder.GenerateElementId(window) : null,
                windowTitle = window?.Title ?? string.Empty
            };
        });
    }

    /// <summary>
    /// Move logical focus to a target element.
    /// </summary>
    public object FocusElement(string? elementId)
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

            if (element is not IInputElement inputElement)
            {
                return new { success = false, error = "Element cannot receive focus" };
            }

            var window = ResolveFocusWindow(element);
            if (window != null)
            {
                FocusManager.SetFocusedElement(window, inputElement);
            }

            if (element is UIElement uiElement)
            {
                uiElement.Focus();
                Keyboard.Focus(uiElement);
            }

            return new
            {
                success = true,
                focused = true,
                focusKind = window != null ? "Logical" : "Keyboard",
                focusedElementId = element is DependencyObject depObj ? _elementFinder.GenerateElementId(depObj) : null
            };
        });
    }

    private static Window? ResolveFocusWindow(object? scopedElement)
    {
        if (scopedElement is Window scopedWindow)
        {
            return scopedWindow;
        }

        if (scopedElement is DependencyObject depObj)
        {
            var ownerWindow = Window.GetWindow(depObj);
            if (ownerWindow != null)
            {
                return ownerWindow;
            }

            var current = depObj;
            while (current != null)
            {
                if (current is Window ancestorWindow)
                {
                    return ancestorWindow;
                }

                current = LogicalTreeHelper.GetParent(current)
                    ?? (current is Visual ? VisualTreeHelper.GetParent(current) : null);
            }
        }

        var application = Application.Current;
        if (application == null || application.Windows.Count == 0)
        {
            return null;
        }

        return application.Windows.OfType<Window>()
            .FirstOrDefault(window => FocusManager.GetFocusedElement(window) != null)
            ?? application.MainWindow
            ?? application.Windows.OfType<Window>().FirstOrDefault();
    }
}
