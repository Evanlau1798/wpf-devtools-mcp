using System.Windows;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class InteractionAnalyzer
{
    private static ToolErrorPayload? GetKeyboardInteractionError(UIElement uiElement, bool requireFocusable)
    {
        if (PresentationSource.FromVisual(uiElement) == null)
        {
            return CreateDetachedVisualTreeError();
        }

        if (!uiElement.IsVisible)
        {
            return ToolErrorFactory.ElementNotLoaded(
                "Element is not visible in the active visual tree",
                "Ensure the element is attached to a rendered visual tree before retrying. If it is inside an inactive TabItem, activate that tab first and then retry.");
        }

        if (!requireFocusable)
        {
            return null;
        }

        if (!uiElement.Focusable || !uiElement.IsEnabled)
        {
            return ToolErrorFactory.InvalidArgument(
                "Element cannot receive keyboard focus",
                "Choose a visible, enabled, focusable control such as TextBox, Button, or ComboBox.");
        }

        return null;
    }

    private static ToolErrorPayload CreateDetachedVisualTreeError()
    {
        return ToolErrorFactory.ElementNotLoaded(
            "Element is not connected to a presentation source",
            "Ensure the element is attached to a rendered visual tree before retrying. If it is inside an inactive TabItem, activate that tab first and then retry.");
    }
}
