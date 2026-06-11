using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private sealed record BindingSourceAnalysis(
        string SourceClassification,
        bool IsSupported,
        string? UnsupportedReason);

    private static BindingSourceAnalysis AnalyzeBindingSource(
        DependencyObject element,
        BindingExpressionBase bindingExpression)
    {
        return bindingExpression switch
        {
            BindingExpression simpleBinding => AnalyzeSimpleBindingSource(element, simpleBinding.ParentBinding),
            MultiBindingExpression multiBinding => AnalyzeMultiBindingSource(element, multiBinding.ParentMultiBinding),
            _ => new BindingSourceAnalysis(
                "UnsupportedBindingType",
                false,
                $"Binding type '{bindingExpression.GetType().Name}' is not supported by affected-element analysis.")
        };
    }

    private static BindingSourceAnalysis AnalyzeSimpleBindingSource(
        DependencyObject element,
        Binding? binding)
    {
        if (binding == null)
        {
            return new BindingSourceAnalysis(
                "Unknown",
                false,
                "Binding declaration metadata was unavailable for affected-element analysis.");
        }

        var explicitSource = TryAnalyzeNonDataContextSource(binding);
        return explicitSource ?? AnalyzeDataContextChain(element);
    }

    private static BindingSourceAnalysis AnalyzeMultiBindingSource(
        DependencyObject element,
        MultiBinding? binding)
    {
        if (binding == null)
        {
            return new BindingSourceAnalysis(
                "Unknown",
                false,
                "MultiBinding declaration metadata was unavailable for affected-element analysis.");
        }

        foreach (var childBindingBase in binding.Bindings)
        {
            if (childBindingBase is not Binding childBinding)
            {
                return new BindingSourceAnalysis(
                    "UnsupportedBindingType",
                    false,
                    "MultiBinding contains a child binding that is not a simple Binding.");
            }

            var explicitSource = TryAnalyzeNonDataContextSource(childBinding);
            if (explicitSource != null)
            {
                return explicitSource;
            }
        }

        return AnalyzeDataContextChain(element);
    }

    private static BindingSourceAnalysis? TryAnalyzeNonDataContextSource(Binding binding)
    {
        if (binding.Source != null)
        {
            return new BindingSourceAnalysis(
                "ExplicitSource",
                false,
                "Binding uses explicit Source instead of the element's DataContext chain.");
        }

        if (!string.IsNullOrWhiteSpace(binding.ElementName))
        {
            return new BindingSourceAnalysis(
                "ElementName",
                false,
                "Binding uses ElementName instead of the element's DataContext chain.");
        }

        if (binding.RelativeSource != null)
        {
            return new BindingSourceAnalysis(
                "RelativeSource",
                false,
                $"Binding uses RelativeSource ({binding.RelativeSource.Mode}) instead of the element's DataContext chain.");
        }

        return null;
    }

    private static BindingSourceAnalysis AnalyzeDataContextChain(DependencyObject element)
    {
        var localState = GetLocalDataContextState(element);
        if (localState.HasLocalValue)
        {
            return localState.Value != null
                ? new BindingSourceAnalysis("LocalDataContext", true, null)
                : new BindingSourceAnalysis(
                    "NoDataContext",
                    false,
                    "Binding uses the element's local DataContext slot, but that slot is null.");
        }

        var inheritedDataContext = FindInheritedDataContext(element);
        return inheritedDataContext != null
            ? new BindingSourceAnalysis("InheritedDataContext", true, null)
            : new BindingSourceAnalysis(
                "NoDataContext",
                false,
                "Binding relies on DataContext inheritance, but no local or inherited DataContext could be proven.");
    }

    private static (bool HasLocalValue, object? Value) GetLocalDataContextState(DependencyObject element)
    {
        return element switch
        {
            FrameworkElement frameworkElement => (
                frameworkElement.ReadLocalValue(FrameworkElement.DataContextProperty) != DependencyProperty.UnsetValue,
                frameworkElement.DataContext),
            FrameworkContentElement frameworkContentElement => (
                frameworkContentElement.ReadLocalValue(FrameworkContentElement.DataContextProperty) != DependencyProperty.UnsetValue,
                frameworkContentElement.DataContext),
            _ => (false, null)
        };
    }

    private static object? FindInheritedDataContext(DependencyObject element)
    {
        var current = GetParentElement(element);
        while (current != null)
        {
            var localState = GetLocalDataContextState(current);
            if (localState.HasLocalValue)
            {
                return localState.Value;
            }

            var currentDataContext = GetCurrentDataContext(current);
            if (currentDataContext != null)
            {
                return currentDataContext;
            }

            current = GetParentElement(current);
        }

        return null;
    }

    private static object? GetCurrentDataContext(DependencyObject element)
    {
        return element switch
        {
            FrameworkElement frameworkElement => frameworkElement.DataContext,
            FrameworkContentElement frameworkContentElement => frameworkContentElement.DataContext,
            _ => null
        };
    }

    private static DependencyObject? GetParentElement(DependencyObject element)
    {
        return element switch
        {
            FrameworkElement frameworkElement => frameworkElement.Parent
                ?? LogicalTreeHelper.GetParent(frameworkElement)
                ?? VisualTreeHelper.GetParent(frameworkElement),
            FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent
                ?? LogicalTreeHelper.GetParent(frameworkContentElement),
            _ => LogicalTreeHelper.GetParent(element) ?? VisualTreeHelper.GetParent(element)
        };
    }
}
