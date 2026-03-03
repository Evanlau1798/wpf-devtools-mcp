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
            return new { success = false, error = "Element not found" };
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
            return new { success = false, error = "Element not found" };
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
            return new { success = false, error = "Element is null" };
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            return new { success = false, error = "propertyName is required" };
        }

        // Find DependencyProperty
        var dp = FindDependencyProperty(element, propertyName);
        if (dp == null)
        {
            return new { success = false, error = $"Property '{propertyName}' not found" };
        }

        // Get binding expression
        var bindingExpr = BindingOperations.GetBindingExpression(element, dp);
        if (bindingExpr == null)
        {
            return new { success = true, hasBinding = false, message = "No binding on this property" };
        }

        // Track binding for leak detection
        var binding = bindingExpr.ParentBinding;
        if (binding != null)
        {
            PerformanceAnalyzer.TrackBinding(binding);
        }

        var chain = new List<object>();

        // Get binding details
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

    /// <summary>
    /// Force binding to update source or target
    /// </summary>
    public object ForceBindingUpdate(DependencyObject element, string propertyName, string direction)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                ForceBindingUpdate(element, propertyName, direction));
        }

        if (element == null)
        {
            return new { success = false, error = "Element is null" };
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            return new { success = false, error = "propertyName is required" };
        }

        // Find DependencyProperty
        var dp = FindDependencyProperty(element, propertyName);
        if (dp == null)
        {
            return new { success = false, error = $"Property '{propertyName}' not found" };
        }

        // Get binding expression
        var bindingExpr = BindingOperations.GetBindingExpression(element, dp);
        if (bindingExpr == null)
        {
            return new { success = false, error = "No binding on this property" };
        }

        // Track binding for leak detection
        var binding = bindingExpr.ParentBinding;
        if (binding != null)
        {
            PerformanceAnalyzer.TrackBinding(binding);
        }

        try
        {
            if (direction?.ToLower() == "source")
            {
                bindingExpr.UpdateSource();
                return new
                {
                    success = true,
                    message = "Binding source updated",
                    direction = "Source",
                    propertyName
                };
            }
            else if (direction?.ToLower() == "target")
            {
                bindingExpr.UpdateTarget();
                return new
                {
                    success = true,
                    message = "Binding target updated",
                    direction = "Target",
                    propertyName
                };
            }
            else
            {
                return new { success = false, error = "Invalid direction. Use 'Source' or 'Target'" };
            }
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to update binding: {ex.Message}" };
        }
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

        // Get all dependency properties using reflection
        var type = element.GetType();
        var dpFields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DependencyProperty));

        foreach (var field in dpFields)
        {
            var dp = field.GetValue(null) as DependencyProperty;
            if (dp != null)
            {
                var bindingExpr = BindingOperations.GetBindingExpression(element, dp);
                if (bindingExpr != null)
                {
                    var binding = bindingExpr.ParentBinding;

                    // Track binding for leak detection
                    if (binding != null)
                    {
                        PerformanceAnalyzer.TrackBinding(binding);
                    }

                    bindings.Add(new
                    {
                        propertyName = dp.Name,
                        path = binding?.Path?.Path,
                        mode = binding?.Mode.ToString(),
                        updateSourceTrigger = binding?.UpdateSourceTrigger.ToString()
                    });
                }
            }
        }

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
