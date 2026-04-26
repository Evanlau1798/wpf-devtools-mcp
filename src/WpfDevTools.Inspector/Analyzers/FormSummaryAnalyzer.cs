using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Produces a semantic summary for form-like WPF subtrees.
/// </summary>
public sealed class FormSummaryAnalyzer : DispatcherAnalyzerBase
{
    private const int MaxTraversalNodes = 512;
    private const int MaxInputs = 128;
    private const int MaxCommands = 128;
    private const int MaxStringValueLength = 512;

    private readonly ElementFinder _elementFinder;

    /// <summary>
    /// Create a form summary analyzer backed by the shared element finder.
    /// </summary>
    public FormSummaryAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Build a structured form summary for the root window or a scoped subtree.
    /// </summary>
    public object GetFormSummary(string? elementId = null, bool includeFramework = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var root = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (root == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var (scopeVisibility, isCurrentlyVisible) = SceneSummaryElementHelpers.GetScopeVisibilityMetadata(root);
            var inputs = new List<object>();
            var commands = new List<object>();
            var emptyInputCount = 0;
            var validationErrorCount = 0;
            var hasReadyPrimaryCommand = false;
            var hasReadyFallbackCommand = false;
            var hasPrimaryCommand = false;
            var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
            var budget = new FormSummaryBudget();

            TraverseDescendantsAndSelf(root, visited, budget, descendant =>
            {
                if (descendant is not FrameworkElement frameworkElement)
                {
                    return;
                }

                var includeElement = SceneSummaryElementHelpers.ShouldIncludeFormSummaryElement(
                    frameworkElement,
                    includeFramework);

                if (includeElement && SceneSummaryElementHelpers.IsInputControl(frameworkElement))
                {
                    if (!budget.TryTakeInput())
                    {
                        return;
                    }

                    var inputSummary = CreateInputSummary(frameworkElement, budget);
                    inputs.Add(inputSummary.Payload);
                    if (inputSummary.IsEmpty)
                    {
                        emptyInputCount++;
                    }

                    validationErrorCount += inputSummary.ValidationErrorCount;
                }

                if (includeElement && frameworkElement is ButtonBase button)
                {
                    if (!budget.TryTakeCommand())
                    {
                        return;
                    }

                    var commandSummary = CreateCommandSummary(button, budget);
                    commands.Add(commandSummary.Payload);
                    if (commandSummary.IsPrimary)
                    {
                        hasPrimaryCommand = true;
                    }

                    if (commandSummary.IsReady)
                    {
                        hasReadyFallbackCommand = true;
                        if (commandSummary.IsPrimary)
                        {
                            hasReadyPrimaryCommand = true;
                        }
                    }
                }
            });

            var validationSubmittable = validationErrorCount == 0;
            var interactionSubmittable = hasReadyPrimaryCommand || (!hasPrimaryCommand && hasReadyFallbackCommand);
            var summary = new
            {
                totalInputs = inputs.Count,
                emptyInputs = emptyInputCount,
                errorCount = validationErrorCount,
                validationSubmittable,
                interactionSubmittable,
                isSubmittable = validationSubmittable && interactionSubmittable
            };

            return new
            {
                success = true,
                formScope = _elementFinder.GenerateElementId(root),
                scopeVisibility,
                isCurrentlyVisible,
                inputs,
                commands,
                traversalNodeCount = budget.TraversalNodeCount,
                omittedNodeCount = budget.OmittedNodeCount,
                omittedInputCount = budget.OmittedInputCount,
                omittedCommandCount = budget.OmittedCommandCount,
                truncated = budget.Truncated,
                truncationReasons = budget.TruncationReasons,
                payloadLimits = CreatePayloadLimits(),
                summary
            };
        });
    }

    private (object Payload, bool IsEmpty, int ValidationErrorCount) CreateInputSummary(
        FrameworkElement element,
        FormSummaryBudget budget)
    {
        var currentValue = SceneSummaryElementHelpers.GetCurrentValue(element);
        var textValue = currentValue?.ToString() ?? string.Empty;
        var validationErrors = Validation.GetErrors(element)
            .Select(error => error.ErrorContent?.ToString() ?? string.Empty)
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => TruncateString(error, budget)!)
            .ToArray();

        return (new
        {
            elementId = _elementFinder.GenerateElementId(element),
            elementType = element.GetType().Name,
            elementName = TruncateString(SceneSummaryElementHelpers.GetElementName(element), budget),
            label = TruncateString(SceneSummaryElementHelpers.TryGetNearbyLabel(element), budget),
            currentValue = TruncatePayloadValue(currentValue, budget),
            bindingPath = TruncateString(SceneSummaryElementHelpers.TryGetBindingPath(element), budget),
            isEmpty = string.IsNullOrWhiteSpace(textValue),
            validationErrors
        }, string.IsNullOrWhiteSpace(textValue), validationErrors.Length);
    }

    private (object Payload, bool IsReady, bool IsPrimary) CreateCommandSummary(
        ButtonBase button,
        FormSummaryBudget budget)
    {
        var (isReady, blockers) = SceneSummaryElementHelpers.EvaluateInteractionReadiness(button);
        var isPrimary = SceneSummaryElementHelpers.IsPrimaryCommand(button);
        return (new
        {
            elementId = _elementFinder.GenerateElementId(button),
            elementType = button.GetType().Name,
            elementName = TruncateString(SceneSummaryElementHelpers.GetElementName(button), budget),
            text = TruncateString(SceneSummaryElementHelpers.GetDisplayText(button), budget),
            isPrimary,
            isReady,
            blockers
        }, isReady, isPrimary);
    }

    private static void TraverseDescendantsAndSelf(
        DependencyObject root,
        HashSet<DependencyObject> visited,
        FormSummaryBudget budget,
        Action<DependencyObject> visit)
    {
        if (!visited.Add(root) || !budget.TryTakeTraversalNode())
        {
            return;
        }

        visit(root);

        var children = SceneSummaryElementHelpers.GetSceneChildren(root);
        for (var index = 0; index < children.Count; index++)
        {
            if (budget.ShouldStopTraversal(out var reason))
            {
                budget.OmitRemainingNodes(children.Count - index, reason);
                break;
            }

            TraverseDescendantsAndSelf(children[index], visited, budget, visit);
        }
    }

    private static object CreatePayloadLimits() => new
    {
        maxTraversalNodes = MaxTraversalNodes,
        maxInputs = MaxInputs,
        maxCommands = MaxCommands,
        maxStringValueLength = MaxStringValueLength
    };

    private static object? TruncatePayloadValue(object? value, FormSummaryBudget budget)
    {
        return value is string text
            ? TruncateString(text, budget)
            : value;
    }

    private static string? TruncateString(string? value, FormSummaryBudget budget)
    {
        if (value == null || value.Length <= MaxStringValueLength)
        {
            return value;
        }

        budget.MarkTruncated("StringValueLength");
        return value[..(MaxStringValueLength - 3)] + "...";
    }

    private sealed class FormSummaryBudget
    {
        private readonly List<string> _truncationReasons = [];
        private bool _commandLimitExceeded;
        private bool _inputLimitExceeded;

        public int TraversalNodeCount { get; private set; }

        public int OmittedNodeCount { get; private set; }

        public int InputCount { get; private set; }

        public int OmittedInputCount { get; private set; }

        public int CommandCount { get; private set; }

        public int OmittedCommandCount { get; private set; }

        public bool Truncated => _truncationReasons.Count > 0;

        public IReadOnlyList<string> TruncationReasons => _truncationReasons;

        public bool TryTakeTraversalNode()
        {
            if (TraversalNodeCount >= MaxTraversalNodes)
            {
                MarkTruncated("TraversalNodeLimit");
                return false;
            }

            TraversalNodeCount++;
            return true;
        }

        public bool TryTakeInput()
        {
            if (InputCount >= MaxInputs)
            {
                OmittedInputCount++;
                _inputLimitExceeded = true;
                MarkTruncated("InputLimit");
                return false;
            }

            InputCount++;
            return true;
        }

        public bool TryTakeCommand()
        {
            if (CommandCount >= MaxCommands)
            {
                OmittedCommandCount++;
                _commandLimitExceeded = true;
                MarkTruncated("CommandLimit");
                return false;
            }

            CommandCount++;
            return true;
        }

        public bool ShouldStopTraversal(out string reason)
        {
            if (TraversalNodeCount >= MaxTraversalNodes)
            {
                reason = "TraversalNodeLimit";
                return true;
            }

            if (_inputLimitExceeded)
            {
                reason = "InputLimit";
                return true;
            }

            if (_commandLimitExceeded)
            {
                reason = "CommandLimit";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        public void OmitRemainingNodes(int count, string reason)
        {
            if (count <= 0)
            {
                return;
            }

            OmittedNodeCount += count;
            MarkTruncated(reason);
        }

        public void MarkTruncated(string reason)
        {
            if (!_truncationReasons.Contains(reason, StringComparer.Ordinal))
            {
                _truncationReasons.Add(reason);
            }
        }
    }
}
