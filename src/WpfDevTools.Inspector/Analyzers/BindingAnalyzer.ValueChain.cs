using System.Windows;
using System.Windows.Data;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private object GetBindingValueChainCore(DependencyObject element, string propertyName)
    {
        if (element == null)
        {
            return ToolErrorFactory.ElementNotFound();
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            return ToolErrorFactory.InvalidArgument(
                "propertyName is required",
                "Provide propertyName to inspect a specific bound DependencyProperty.");
        }

        var dp = FindDependencyProperty(element, propertyName);
        if (dp == null)
        {
            return ToolErrorFactory.PropertyNotFound(propertyName, element.GetType().Name);
        }

        var bindingExpression = BindingOperations.GetBindingExpressionBase(element, dp);
        if (bindingExpression == null)
        {
            return new { success = true, hasBinding = false, message = "No binding on this property" };
        }

        var chain = new List<object>
        {
            BuildBindingDefinitionStep(element, dp, bindingExpression)
        };

        if (bindingExpression is MultiBindingExpression multiBindingExpression)
        {
            AppendMultiBindingInputSteps(chain, element, multiBindingExpression);
        }

        AppendDataContextSteps(chain, element);
        chain.Add(BuildResolvedSourceStep(bindingExpression));
        chain.Add(BuildFinalValueStep(element.GetValue(dp)));

        return new
        {
            success = true,
            hasBinding = true,
            propertyName,
            chainLength = chain.Count,
            chain
        };
    }

    private static Dictionary<string, object?> BuildBindingDefinitionStep(
        DependencyObject element,
        DependencyProperty dp,
        BindingExpressionBase bindingExpression)
    {
        var payload = BuildBindingPayload(element, dp, bindingExpression);
        payload["diagnosticKind"] = "BindingValueResolutionStep";
        payload["sourceKind"] = "BindingDefinition";
        payload["step"] = "Binding";
        payload["fallbackValue"] = bindingExpression switch
        {
            BindingExpression simpleBinding => FormatResponseValue(simpleBinding.ParentBinding?.FallbackValue),
            MultiBindingExpression multiBinding => FormatResponseValue(multiBinding.ParentMultiBinding?.FallbackValue),
            _ => null
        };
        payload.Remove("propertyName");
        payload.Remove("currentValue");
        return payload;
    }

    private static void AppendMultiBindingInputSteps(
        List<object> chain,
        DependencyObject element,
        MultiBindingExpression bindingExpression)
    {
        var childBindings = bindingExpression.ParentMultiBinding?.Bindings.OfType<Binding>().ToArray();
        if (childBindings == null || childBindings.Length == 0)
        {
            return;
        }

        for (var index = 0; index < childBindings.Length; index++)
        {
            var childBinding = childBindings[index];
            var childValue = TryResolveBindingInputValue(element, childBinding);
            chain.Add(new Dictionary<string, object?>
            {
                ["diagnosticKind"] = "BindingValueResolutionStep",
                ["sourceKind"] = "BindingInput",
                ["step"] = "BindingInput",
                ["bindingIndex"] = index,
                ["path"] = childBinding.Path?.Path,
                ["mode"] = childBinding.Mode.ToString(),
                ["updateSourceTrigger"] = childBinding.UpdateSourceTrigger.ToString(),
                ["converter"] = childBinding.Converter?.GetType().Name,
                ["valueType"] = childValue?.GetType().Name,
                ["value"] = FormatResponseValue(childValue),
                ["resolutionState"] = childValue == null ? "Unresolved" : "Resolved"
            });
        }
    }

    private static object? TryResolveBindingInputValue(DependencyObject element, Binding binding)
    {
        var source = binding.Source;
        if (source == null && element is FrameworkElement frameworkElement)
        {
            source = frameworkElement.DataContext;
        }

        if (source == null || string.IsNullOrWhiteSpace(binding.Path?.Path))
        {
            return source;
        }

        var property = source.GetType().GetProperty(binding.Path.Path);
        return property?.GetValue(source);
    }

    private static void AppendDataContextSteps(List<object> chain, DependencyObject element)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return;
        }

        var localDataContextValue = frameworkElement.ReadLocalValue(FrameworkElement.DataContextProperty);
        var hasLocalDataContext = localDataContextValue != DependencyProperty.UnsetValue;
        var localDataContext = hasLocalDataContext ? localDataContextValue : frameworkElement.DataContext;

        chain.Add(new Dictionary<string, object?>
        {
            ["diagnosticKind"] = "BindingValueResolutionStep",
            ["sourceKind"] = "LocalDataContext",
            ["step"] = "LocalDataContext",
            ["elementType"] = frameworkElement.GetType().Name,
            ["hasLocalValue"] = hasLocalDataContext,
            ["hasDataContext"] = localDataContext != null,
            ["dataContextType"] = localDataContext?.GetType().Name,
            ["dataContextValue"] = FormatResponseValue(localDataContext)
        });

        for (var current = frameworkElement.Parent as FrameworkElement; current != null; current = current.Parent as FrameworkElement)
        {
            if (current.DataContext == null)
            {
                continue;
            }

            chain.Add(new Dictionary<string, object?>
            {
                ["diagnosticKind"] = "BindingValueResolutionStep",
                ["sourceKind"] = "InheritedDataContext",
                ["step"] = "InheritedDataContext",
                ["elementType"] = current.GetType().Name,
                ["dataContextType"] = current.DataContext.GetType().Name,
                ["dataContextValue"] = FormatResponseValue(current.DataContext)
            });
            break;
        }
    }

    private static Dictionary<string, object?> BuildResolvedSourceStep(BindingExpressionBase bindingExpression)
    {
        var resolvedSource = bindingExpression switch
        {
            BindingExpression simpleBinding => simpleBinding.ResolvedSource,
            MultiBindingExpression multiBinding => multiBinding.ParentMultiBinding,
            _ => null
        };

        return new Dictionary<string, object?>
        {
            ["diagnosticKind"] = "BindingValueResolutionStep",
            ["sourceKind"] = "ResolvedSource",
            ["step"] = "ResolvedSource",
            ["sourceType"] = resolvedSource?.GetType().Name,
            ["sourceValue"] = FormatResponseValue(resolvedSource),
            ["resolutionState"] = resolvedSource == null ? "Unresolved" : "Resolved"
        };
    }

    private static Dictionary<string, object?> BuildFinalValueStep(object? finalValue)
    {
        return new Dictionary<string, object?>
        {
            ["diagnosticKind"] = "BindingValueResolutionStep",
            ["sourceKind"] = "FinalValue",
            ["step"] = "FinalValue",
            ["valueType"] = finalValue?.GetType().Name,
            ["value"] = FormatResponseValue(finalValue)
        };
    }
}
