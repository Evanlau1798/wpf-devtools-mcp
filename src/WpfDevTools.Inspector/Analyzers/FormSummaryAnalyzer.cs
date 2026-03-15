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

            var inputs = new List<object>();
            var commands = new List<object>();
            var emptyInputCount = 0;
            var validationErrorCount = 0;
            var hasReadyPrimaryCommand = false;
            var hasReadyFallbackCommand = false;
            var hasPrimaryCommand = false;
            var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);

            foreach (var descendant in EnumerateDescendantsAndSelf(root, visited))
            {
                if (descendant is not FrameworkElement frameworkElement)
                {
                    continue;
                }

                var includeElement = SceneSummaryElementHelpers.ShouldIncludeFormSummaryElement(
                    frameworkElement,
                    includeFramework);

                if (includeElement && SceneSummaryElementHelpers.IsInputControl(frameworkElement))
                {
                    var inputSummary = CreateInputSummary(frameworkElement);
                    inputs.Add(inputSummary.Payload);
                    if (inputSummary.IsEmpty)
                    {
                        emptyInputCount++;
                    }

                    validationErrorCount += inputSummary.ValidationErrorCount;
                }

                if (includeElement && frameworkElement is ButtonBase button)
                {
                    var commandSummary = CreateCommandSummary(button);
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
            }

            var summary = new
            {
                totalInputs = inputs.Count,
                emptyInputs = emptyInputCount,
                errorCount = validationErrorCount,
                isSubmittable = validationErrorCount == 0
                    && (hasReadyPrimaryCommand || (!hasPrimaryCommand && hasReadyFallbackCommand))
            };

            return new
            {
                success = true,
                formScope = _elementFinder.GenerateElementId(root),
                inputs,
                commands,
                summary
            };
        });
    }

    private (object Payload, bool IsEmpty, int ValidationErrorCount) CreateInputSummary(FrameworkElement element)
    {
        var currentValue = SceneSummaryElementHelpers.GetCurrentValue(element);
        var textValue = currentValue?.ToString() ?? string.Empty;
        var validationErrors = Validation.GetErrors(element)
            .Select(error => error.ErrorContent?.ToString() ?? string.Empty)
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .ToArray();

        return (new
        {
            elementId = _elementFinder.GenerateElementId(element),
            elementType = element.GetType().Name,
            elementName = SceneSummaryElementHelpers.GetElementName(element),
            label = SceneSummaryElementHelpers.TryGetNearbyLabel(element),
            currentValue,
            bindingPath = SceneSummaryElementHelpers.TryGetBindingPath(element),
            isEmpty = string.IsNullOrWhiteSpace(textValue),
            validationErrors
        }, string.IsNullOrWhiteSpace(textValue), validationErrors.Length);
    }

    private (object Payload, bool IsReady, bool IsPrimary) CreateCommandSummary(ButtonBase button)
    {
        var (isReady, blockers) = SceneSummaryElementHelpers.EvaluateInteractionReadiness(button);
        var isPrimary = SceneSummaryElementHelpers.IsPrimaryCommand(button);
        return (new
        {
            elementId = _elementFinder.GenerateElementId(button),
            elementType = button.GetType().Name,
            elementName = SceneSummaryElementHelpers.GetElementName(button),
            text = SceneSummaryElementHelpers.GetDisplayText(button),
            isPrimary,
            isReady,
            blockers
        }, isReady, isPrimary);
    }

    private static IEnumerable<DependencyObject> EnumerateDescendantsAndSelf(
        DependencyObject root,
        HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            yield break;
        }

        yield return root;

        foreach (var child in SceneSummaryElementHelpers.GetSceneChildren(root))
        {
            foreach (var descendant in EnumerateDescendantsAndSelf(child, visited))
            {
                yield return descendant;
            }
        }
    }
}
