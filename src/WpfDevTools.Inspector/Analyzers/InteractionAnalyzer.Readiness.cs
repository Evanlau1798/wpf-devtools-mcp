using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class InteractionAnalyzer
{
    /// <summary>
    /// Diagnose whether a runtime element is currently ready for interaction.
    /// </summary>
    public object GetInteractionReadiness(string? elementId, string? interactionType = null)
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

            if (element is not FrameworkElement frameworkElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target before checking interaction readiness.");
            }

            var normalizedInteractionType = string.IsNullOrWhiteSpace(interactionType) ? "Click" : interactionType!;
            var blockers = new List<object>();
            InactiveTabActivationGuidance? activationGuidance = null;

            if (!frameworkElement.IsEnabled)
            {
                blockers.Add(CreateBlocker("ElementDisabled", "Element IsEnabled is false."));
            }

            if (frameworkElement.Visibility != Visibility.Visible)
            {
                blockers.Add(CreateBlocker("ElementHidden", $"Element Visibility is {frameworkElement.Visibility}."));
            }

            if (frameworkElement.Opacity <= 0)
            {
                blockers.Add(CreateBlocker("ElementTransparent", "Element Opacity is 0."));
            }

            if (!frameworkElement.IsHitTestVisible)
            {
                blockers.Add(CreateBlocker("HitTestingDisabled", "Element IsHitTestVisible is false."));
            }

            if (frameworkElement.ActualWidth <= 0 || frameworkElement.ActualHeight <= 0)
            {
                var reason = SceneSummaryElementHelpers.GetLayoutSizeBlockerReason(frameworkElement);
                var message = reason == "ElementInInactiveTab"
                    ? "Element belongs to an inactive TabItem and has not been rendered into the active visual tree yet."
                    : "Element has zero ActualWidth or ActualHeight.";
                if (reason == "ElementInInactiveTab")
                {
                    activationGuidance = InactiveTabActivationGuidanceBuilder.TryBuild(
                        frameworkElement,
                        dependencyObject => _elementFinder.GenerateElementId(dependencyObject));
                }

                blockers.Add(CreateBlocker(reason, message, activationGuidance));
            }

            if (frameworkElement is ButtonBase button && button.Command is ICommand command)
            {
                var canExecute = command.CanExecute(button.CommandParameter);
                if (!canExecute)
                {
                    blockers.Add(CreateBlocker("CommandCannotExecute", "The bound ICommand.CanExecute returned false."));
                }
            }

            return new
            {
                success = true,
                elementId = elementId ?? _elementFinder.GenerateElementId(frameworkElement),
                interactionType = normalizedInteractionType,
                isReady = blockers.Count == 0,
                blockers,
                activationPath = activationGuidance?.ActivationPath,
                activationTarget = activationGuidance is null
                    ? null
                    : new
                    {
                        tabItemElementId = activationGuidance.TabItemElementId,
                        tabItemName = activationGuidance.TabItemName
                    },
                elementState = new
                {
                    isEnabled = frameworkElement.IsEnabled,
                    visibility = frameworkElement.Visibility.ToString(),
                    opacity = frameworkElement.Opacity,
                    isHitTestVisible = frameworkElement.IsHitTestVisible,
                    hasSize = frameworkElement.ActualWidth > 0 && frameworkElement.ActualHeight > 0
                }
            };
        });
    }

    private static object CreateBlocker(
        string reason,
        string message,
        InactiveTabActivationGuidance? activationGuidance = null) =>
        activationGuidance is null
            ? new
            {
                reason,
                message
            }
            : new
            {
                reason,
                message,
                activationPath = activationGuidance.ActivationPath,
                tabItemElementId = activationGuidance.TabItemElementId
            };
}
