using System.Windows;
using System.Windows.Data;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Bindings
/// </summary>
public class BindingAnalyzer
{
    /// <summary>
    /// Get all bindings for an element
    /// </summary>
    public object GetBindings(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetBindings(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        var bindings = new List<object>();

        // Get all dependency properties with bindings
        if (element is DependencyObject depObj)
        {
            var properties = GetDependencyPropertiesWithBindings(depObj);
            bindings.AddRange(properties);
        }

        return new { bindings };
    }

    /// <summary>
    /// Get binding errors from trace
    /// </summary>
    public object GetBindingErrors()
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetBindingErrors());
        }

        // TODO: Implement binding error detection using PresentationTraceSources
        // For now, return placeholder
        return new { errors = new List<object>() };
    }

    /// <summary>
    /// Get DataContext chain for an element
    /// </summary>
    public object GetDataContextChain(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetDataContextChain(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        var chain = new List<object>();

        // Walk up the tree collecting DataContext
        if (element is FrameworkElement fe)
        {
            var current = fe;
            while (current != null)
            {
                var dataContext = current.DataContext;
                chain.Add(new
                {
                    elementType = current.GetType().Name,
                    elementName = current.Name,
                    dataContextType = dataContext?.GetType().Name,
                    hasDataContext = dataContext != null
                });

                current = current.Parent as FrameworkElement;
            }
        }

        return new { chain };
    }

    /// <summary>
    /// Get binding value resolution chain from source to target
    /// </summary>
    public object GetBindingValueChain(DependencyObject element, string propertyName)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetBindingValueChain(element, propertyName));
        }

        if (element == null)
        {
            return new { error = "Element is null" };
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            return new { error = "propertyName is required" };
        }

        // Find DependencyProperty
        var dp = FindDependencyProperty(element, propertyName);
        if (dp == null)
        {
            return new { error = $"Property '{propertyName}' not found" };
        }

        // Get binding expression
        var bindingExpr = BindingOperations.GetBindingExpression(element, dp);
        if (bindingExpr == null)
        {
            return new { success = true, hasBinding = false, message = "No binding on this property" };
        }

        var chain = new List<object>();

        // Get binding details
        var binding = bindingExpr.ParentBinding;
        chain.Add(new
        {
            step = "Binding",
            path = binding.Path?.Path,
            mode = binding.Mode.ToString(),
            updateSourceTrigger = binding.UpdateSourceTrigger.ToString(),
            converter = binding.Converter?.GetType().Name
        });

        // Get data context chain
        var current = element as FrameworkElement;
        while (current != null)
        {
            if (current.DataContext != null)
            {
                chain.Add(new
                {
                    step = "DataContext",
                    elementType = current.GetType().Name,
                    dataContextType = current.DataContext.GetType().Name,
                    dataContextValue = current.DataContext.ToString()
                });
                break;
            }
            current = current.Parent as FrameworkElement;
        }

        // Get resolved value
        var resolvedValue = bindingExpr.ResolvedSource;
        if (resolvedValue != null)
        {
            chain.Add(new
            {
                step = "ResolvedSource",
                sourceType = resolvedValue.GetType().Name,
                sourceValue = resolvedValue.ToString()
            });
        }

        // Get final value
        var finalValue = element.GetValue(dp);
        chain.Add(new
        {
            step = "FinalValue",
            valueType = finalValue?.GetType().Name,
            value = finalValue?.ToString()
        });

        return new
        {
            success = true,
            hasBinding = true,
            propertyName,
            chainLength = chain.Count,
            chain
        };
    }

    private DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    private DependencyObject? FindElementById(string elementId)
    {
        // TODO: Implement element lookup by ID
        return GetRootElement();
    }

    private List<object> GetDependencyPropertiesWithBindings(DependencyObject element)
    {
        var bindings = new List<object>();

        // TODO: Enumerate all dependency properties and check for bindings
        // This requires reflection or using BindingOperations.GetBinding()
        // For now, return empty list

        return bindings;
    }

    private DependencyProperty? FindDependencyProperty(DependencyObject element, string propertyName)
    {
        var type = element.GetType();
        var fieldName = propertyName + "Property";

        while (type != null && type != typeof(object))
        {
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);

            if (field != null && field.FieldType == typeof(DependencyProperty))
            {
                return field.GetValue(null) as DependencyProperty;
            }

            type = type.BaseType;
        }

        return null;
    }
}
