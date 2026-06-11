using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private sealed record LiveBindingErrorScanResult(
        IReadOnlyList<BindingErrorInfo> Errors,
        BindingScanBudget Budget);

    private LiveBindingErrorScanResult GetLiveBindingErrors(
        int? maxLiveScanNodes,
        int? maxLiveErrors)
    {
        var rootElement = ResolveElement(elementId: null);
        var errors = new List<BindingErrorInfo>();
        var scannedElements = new HashSet<string>(StringComparer.Ordinal);
        var budget = new BindingScanBudget(
            ResolveLimit(maxLiveScanNodes, DefaultLiveBindingTraversalNodeLimit),
            ResolveLimit(maxLiveErrors, DefaultLiveBindingErrorLimit),
            "LiveBindingTraversalNodeLimit",
            "LiveBindingResultLimit");

        if (rootElement != null)
        {
            CollectLiveBindingErrorsFromScope(rootElement, errors, scannedElements, budget);
            return new LiveBindingErrorScanResult(errors, budget);
        }

        foreach (var trackedElement in _elementFinder.GetTrackedElements())
        {
            CollectLiveBindingErrorsFromScope(trackedElement, errors, scannedElements, budget);
        }

        IReadOnlyList<BindingErrorInfo> result = errors.Count == 0 ? Array.Empty<BindingErrorInfo>() : errors;
        return new LiveBindingErrorScanResult(result, budget);
    }

    private void CollectLiveBindingErrorsFromScope(
        DependencyObject scope,
        List<BindingErrorInfo> errors,
        HashSet<string> scannedElements,
        BindingScanBudget budget)
    {
        foreach (var element in DependencyObjectTraversal.EnumerateDescendantsAndSelf(scope))
        {
            if (!budget.TryTakeTraversalNode())
            {
                break;
            }

            var elementId = _elementFinder.GenerateElementId(element);
            if (!scannedElements.Add(elementId))
            {
                continue;
            }

            CollectLocalBindingErrors(element, errors, budget);
        }
    }

    private void CollectLocalBindingErrors(
        DependencyObject element,
        List<BindingErrorInfo> errors,
        BindingScanBudget budget)
    {
        foreach (var property in GetCandidateDependencyProperties(element))
        {
            var bindingExpression = BindingOperations.GetBindingExpression(element, property);
            if (bindingExpression == null || !IsBindingError(bindingExpression))
            {
                continue;
            }

            var error = new BindingErrorInfo
            {
                Timestamp = DateTime.UtcNow,
                Message = BuildLiveBindingErrorMessage(element, property, bindingExpression),
                EventType = bindingExpression.Status.ToString(),
                SourceId = 0,
                Origin = BindingErrorInfo.OriginBindingExpression,
                ElementId = _elementFinder.GenerateElementId(element),
                PropertyName = property.Name,
                BindingPath = bindingExpression.ParentBinding?.Path?.Path
            };

            if (budget.TryTakeResult())
            {
                errors.Add(error);
            }
        }
    }

    private static bool IsBindingError(BindingExpression bindingExpression)
    {
        if (IsValidationOnlyError(bindingExpression))
        {
            return false;
        }

        return bindingExpression.HasError
            || bindingExpression.Status == BindingStatus.PathError
            || bindingExpression.Status == BindingStatus.UpdateTargetError
            || bindingExpression.Status == BindingStatus.UpdateSourceError
            || bindingExpression.Status == BindingStatus.Unattached
            || HasUnresolvableBindingPath(bindingExpression);
    }

    private static bool IsValidationOnlyError(BindingExpression bindingExpression)
    {
        return bindingExpression.HasValidationError
            && bindingExpression.DataItem != null
            && !HasUnresolvableBindingPath(bindingExpression);
    }

    private static bool HasUnresolvableBindingPath(BindingExpression bindingExpression)
    {
        var bindingPath = bindingExpression.ParentBinding?.Path?.Path;
        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return false;
        }

        var resolvedBindingPath = bindingPath!;

        if (IsComplexBindingPath(resolvedBindingPath))
        {
            return false;
        }

        if (bindingExpression.DataItem == null)
        {
            return true;
        }

        return !CanResolveBindingPath(bindingExpression.DataItem, resolvedBindingPath);
    }

    private static bool IsComplexBindingPath(string bindingPath)
    {
        return bindingPath.Contains('[')
            || bindingPath.Contains(']')
            || bindingPath.Contains('(')
            || bindingPath.Contains(')')
            || bindingPath.Contains('/');
    }

    private static bool CanResolveBindingPath(object source, string bindingPath)
    {
        object? current = source;
        foreach (var rawSegment in bindingPath.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                continue;
            }

            if (current == null || !TryGetPropertyValue(current, segment, out current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetPropertyValue(object source, string propertyName, out object? value)
    {
        var descriptor = TypeDescriptor.GetProperties(source).Find(propertyName, ignoreCase: false)
            ?? TypeDescriptor.GetProperties(source).Find(propertyName, ignoreCase: true);
        if (descriptor != null)
        {
            value = descriptor.GetValue(source);
            return true;
        }

        value = null;
        return false;
    }

    private static string BuildLiveBindingErrorMessage(
        DependencyObject element,
        DependencyProperty property,
        BindingExpression bindingExpression)
    {
        var elementType = element.GetType().Name;
        var bindingPath = bindingExpression.ParentBinding?.Path?.Path ?? "(no path)";
        var validationMessage = bindingExpression.ValidationError?.ErrorContent?.ToString();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            return $"Binding error on {elementType}.{property.Name} path '{bindingPath}' ({bindingExpression.Status}): {validationMessage}";
        }

        if (bindingExpression.DataItem == null)
        {
            return $"Binding error on {elementType}.{property.Name} path '{bindingPath}' ({bindingExpression.Status}): no DataContext or resolved source.";
        }

        if (HasUnresolvableBindingPath(bindingExpression))
        {
            return $"Binding error on {elementType}.{property.Name} path '{bindingPath}' ({bindingExpression.Status}): path could not be resolved on the current source object.";
        }

        return $"Binding error on {elementType}.{property.Name} path '{bindingPath}' ({bindingExpression.Status}).";
    }

}
