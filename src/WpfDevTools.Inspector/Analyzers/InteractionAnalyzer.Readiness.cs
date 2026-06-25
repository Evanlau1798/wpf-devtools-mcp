using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
            var element = ResolveElement(elementId);

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

            if (IsClickInteraction(normalizedInteractionType)
                && frameworkElement is not ButtonBase
                && frameworkElement is not TabItem)
            {
                blockers.Add(CreateBlocker(
                    "ClickTargetUnsupported",
                    "click_element supports ButtonBase and TabItem targets. Choose a clickable child, use focus_element, or inspect the element snapshot before interacting."));
            }

            var resolvedElementId = elementId ?? _elementFinder.GenerateElementId(frameworkElement);
            var commandReadiness = CreateCommandReadiness(frameworkElement, resolvedElementId, out var canExecute);
            if (canExecute == false)
            {
                blockers.Add(CreateBlocker("CommandCannotExecute", "The bound ICommand.CanExecute returned false."));
            }

            return new
            {
                success = true,
                elementId = resolvedElementId,
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
                },
                commandReadiness
            };
        });
    }

    private static bool IsClickInteraction(string interactionType) =>
        string.Equals(interactionType, "Click", StringComparison.OrdinalIgnoreCase)
        || string.Equals(interactionType, "MouseClick", StringComparison.OrdinalIgnoreCase)
        || string.Equals(interactionType, "ClickElement", StringComparison.OrdinalIgnoreCase);

    private static object CreateCommandReadiness(
        FrameworkElement frameworkElement,
        string sourceElementId,
        out bool? canExecute)
    {
        canExecute = null;
        var sourceElementType = frameworkElement.GetType().Name;
        if (frameworkElement is not ButtonBase button)
        {
            return new
            {
                hasCommand = false,
                sourceElementId,
                sourceElementType,
                commandName = (string?)null,
                commandNameSource = "None",
                canExecute,
                commandParameterKind = "None",
                riskNotes = new[] { "ElementIsNotButtonBase" }
            };
        }

        if (button.Command is not ICommand command)
        {
            return new
            {
                hasCommand = false,
                sourceElementId,
                sourceElementType,
                commandName = (string?)null,
                commandNameSource = "None",
                canExecute,
                commandParameterKind = GetCommandParameterKind(button.CommandParameter),
                riskNotes = BuildCommandRiskNotes(button.CommandParameter, hasCommand: false, commandNameSource: "None")
            };
        }

        var (commandName, commandNameSource) = GetCommandName(button, command);
        canExecute = command.CanExecute(button.CommandParameter);

        return new
        {
            hasCommand = true,
            sourceElementId,
            sourceElementType,
            commandName,
            commandNameSource,
            canExecute,
            commandParameterKind = GetCommandParameterKind(button.CommandParameter),
            riskNotes = BuildCommandRiskNotes(button.CommandParameter, hasCommand: true, commandNameSource)
        };
    }

    private static (string CommandName, string CommandNameSource) GetCommandName(ButtonBase button, ICommand command)
    {
        var bindingPath = BindingOperations
            .GetBindingExpression(button, ButtonBase.CommandProperty)
            ?.ParentBinding
            .Path
            ?.Path;
        if (!string.IsNullOrWhiteSpace(bindingPath))
        {
            return (bindingPath!, "BindingPath");
        }

        if (command is RoutedCommand routedCommand && !string.IsNullOrWhiteSpace(routedCommand.Name))
        {
            return (routedCommand.Name, "RoutedCommandName");
        }

        return (command.GetType().Name, "CommandType");
    }

    private static string GetCommandParameterKind(object? parameter)
        => parameter switch
        {
            null => "None",
            string => "String",
            bool => "Boolean",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => "Number",
            Enum => "Enum",
            DependencyObject => "WpfObject",
            _ => "Object"
        };

    private static string[] BuildCommandRiskNotes(
        object? commandParameter,
        bool hasCommand,
        string commandNameSource)
    {
        var notes = new List<string>();
        if (!hasCommand)
        {
            notes.Add("NoButtonCommand");
        }
        else
        {
            notes.Add("CanExecuteEvaluatedWithoutExecution");
        }

        if (commandParameter is not null)
        {
            notes.Add("CommandParameterValueRedacted");
        }

        if (commandNameSource == "CommandType")
        {
            notes.Add("CommandNameDerivedFromType");
        }

        return notes.ToArray();
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
