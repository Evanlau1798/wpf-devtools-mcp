using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Shared utility for invoking ButtonBase.OnClick() via reflection.
/// OnClick() is the canonical way to trigger the full WPF click pipeline:
/// it raises the Click RoutedEvent AND executes the bound ICommand.
/// </summary>
internal static class ButtonBaseClickHelper
{
    /// <summary>
    /// Cached MethodInfo for ButtonBase.OnClick (protected virtual).
    /// MethodInfo.Invoke respects virtual dispatch, so invoking this on a
    /// derived instance (Button, ToggleButton, etc.) calls the correct override.
    /// </summary>
    private static readonly MethodInfo? OnClickMethod = typeof(ButtonBase).GetMethod(
        "OnClick",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Invoke OnClick() on a ButtonBase instance, triggering the full click
    /// pipeline (Click event + ICommand execution). Falls back to manual
    /// RaiseEvent + Command.Execute if reflection fails.
    /// </summary>
    internal static void InvokeOnClick(ButtonBase button)
    {
        if (OnClickMethod != null)
        {
            OnClickMethod.Invoke(button, null);
            return;
        }

        // Fallback: raise event + execute command manually
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
        {
            button.Command.Execute(button.CommandParameter);
        }
    }
}
