using System.Windows;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class InteractionAnalyzer
{
    private ToolErrorPayload? GetKeyboardInteractionError(UIElement uiElement, bool requireFocusable)
    {
        if (PresentationSource.FromVisual(uiElement) == null)
        {
            return CreateDetachedVisualTreeError(uiElement);
        }

        if (!uiElement.IsVisible)
        {
            return CreateElementNotLoadedWithActivationHint(
                "Element is not visible in the active visual tree",
                "Ensure the element is attached to a rendered visual tree before retrying. If it is inside an inactive TabItem, activate that tab first and then retry.",
                uiElement);
        }

        if (!requireFocusable)
        {
            return null;
        }

        if (!uiElement.IsEnabled)
        {
            return ToolErrorFactory.InvalidArgument(
                "Element cannot receive keyboard focus",
                $"The target {uiElement.GetType().Name} is resolved but IsEnabled is false. Call get_interaction_readiness for blockers, enable or activate the owning UI state, then retry focus_element.");
        }

        if (!uiElement.Focusable)
        {
            return ToolErrorFactory.InvalidArgument(
                "Element cannot receive keyboard focus",
                $"The target {uiElement.GetType().Name} is resolved but Focusable is false. Call get_interaction_readiness for blockers, target a focusable descendant, or verify the control template before retrying focus_element.");
        }

        return null;
    }

    private ToolErrorPayload CreateDetachedVisualTreeError(UIElement? uiElement = null)
    {
        if (uiElement != null)
        {
            return CreateElementNotLoadedWithActivationHint(
                "Element is not connected to a presentation source",
                "Ensure the element is attached to a rendered visual tree before retrying. If it is inside an inactive TabItem, activate that tab first and then retry.",
                uiElement);
        }

        return ToolErrorFactory.ElementNotLoaded(
            "Element is not connected to a presentation source",
            "Ensure the element is attached to a rendered visual tree before retrying. If it is inside an inactive TabItem, activate that tab first and then retry.");
    }

    private ToolErrorPayload CreateElementNotLoadedWithActivationHint(string message, string defaultHint, UIElement uiElement)
    {
        if (uiElement is not FrameworkElement frameworkElement)
        {
            return ToolErrorFactory.ElementNotLoaded(message, defaultHint);
        }

        var guidance = InactiveTabActivationGuidanceBuilder.TryBuild(
            frameworkElement,
            dependencyObject => _elementFinder.GenerateElementId(dependencyObject));
        if (guidance == null)
        {
            return ToolErrorFactory.ElementNotLoaded(message, defaultHint);
        }

        var hint = $"{defaultHint} Activation path: {guidance.ActivationPath}.";
        return ToolErrorFactory.ElementNotLoaded(
            message,
            hint,
            new
            {
                notRenderedReason = "ElementInInactiveTab",
                activationPath = guidance.ActivationPath,
                tabItemElementId = guidance.TabItemElementId,
                tabItemName = guidance.TabItemName,
                recommended = new
                {
                    tool = "click_element",
                    @params = new
                    {
                        elementId = guidance.TabItemElementId
                    },
                    reason = "Activate the containing TabItem and retry the interaction."
                }
            });
    }
}
