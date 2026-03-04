using System.Windows;
using System.Windows.Data;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Bindings
/// </summary>
public class BindingAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    internal BindingAnalyzer() : this(new ElementFinder())
    {
    }

    public BindingAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get all bindings for an element
    /// </summary>
    public object GetBindings(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
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

            return new { success = true, bindings };
        });
    }

    /// <summary>
    /// Get binding errors captured by PresentationTraceSources.
    /// Installs the trace listener if not already installed.
    /// </summary>
    public object GetBindingErrors(bool clearAfterRead = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            // Ensure trace listener is installed
            BindingErrorTraceListener.Install();

            var errors = BindingErrorTraceListener.Instance.GetErrors();

            var result = new
            {
                success = true,
                errorCount = errors.Count,
                errors = errors.Select(e => new
                {
                    timestamp = e.Timestamp.ToString("O"),
                    message = e.Message,
                    eventType = e.EventType,
                    sourceId = e.SourceId
                }).ToList()
            };

            if (clearAfterRead)
            {
                BindingErrorTraceListener.Instance.ClearErrors();
            }

            return result;
        });
    }

    /// <summary>
    /// Get DataContext chain for an element
    /// </summary>
    public object GetDataContextChain(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
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

            return new { success = true, chain };
        });
    }

    /// <summary>
    /// Get binding value resolution chain from source to target
    /// </summary>
    public object GetBindingValueChain(DependencyObject element, string propertyName)
    {
        return InvokeOnUIThread<object>(() => GetBindingValueChainCore(element, propertyName));
    }

    /// <summary>
    /// Force binding to update source or target
    /// </summary>
    public object ForceBindingUpdate(DependencyObject element, string propertyName, string direction)
    {
        return InvokeOnUIThread<object>(() => ForceBindingUpdateCore(element, propertyName, direction));
    }

    /// <summary>
    /// Get binding value chain by elementId (resolves element on UI thread)
    /// </summary>
    public object GetBindingValueChain(string? elementId, string propertyName)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? GetRootElement()
                : FindElementById(elementId);

            if (element == null)
            {
                return new { success = false, error = "Element not found" };
            }

            return GetBindingValueChainCore(element, propertyName);
        });
    }

    /// <summary>
    /// Force binding update by elementId (resolves element on UI thread)
    /// </summary>
    public object ForceBindingUpdate(string? elementId, string propertyName, string direction)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? GetRootElement()
                : FindElementById(elementId);

            if (element == null)
            {
                return new { success = false, error = "Element not found" };
            }

            return ForceBindingUpdateCore(element, propertyName, direction);
        });
    }

    /// <summary>
    /// Core implementation for GetBindingValueChain. Must be called on the UI thread.
    /// </summary>
    private object GetBindingValueChainCore(DependencyObject element, string propertyName)
    {
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

        var chain = new List<object>();

        // Get binding details
        var binding = bindingExpr.ParentBinding;
        if (binding != null)
        {
            chain.Add(new
            {
                step = "Binding",
                path = binding.Path?.Path,
                mode = binding.Mode.ToString(),
                updateSourceTrigger = binding.UpdateSourceTrigger.ToString(),
                converter = binding.Converter?.GetType().Name
            });
        }

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
    /// Core implementation for ForceBindingUpdate. Must be called on the UI thread.
    /// </summary>
    private object ForceBindingUpdateCore(DependencyObject element, string propertyName, string direction)
    {
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

        try
        {
            if (string.Equals(direction, "source", StringComparison.OrdinalIgnoreCase))
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
            else if (string.Equals(direction, "target", StringComparison.OrdinalIgnoreCase))
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
        return _elementFinder.GetRootElement();
    }

    private DependencyObject? FindElementById(string elementId)
    {
        return _elementFinder.FindById(elementId);
    }

    private List<object> GetDependencyPropertiesWithBindings(DependencyObject element)
    {
        var bindings = new List<object>();
        var seenProperties = new HashSet<string>();

        // Walk up the type hierarchy to find all DependencyProperty fields
        var type = element.GetType();
        while (type != null && type != typeof(object))
        {
            var dpFields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(f => f.FieldType == typeof(DependencyProperty));

            foreach (var field in dpFields)
            {
                var dp = field.GetValue(null) as DependencyProperty;
                if (dp != null && seenProperties.Add(dp.Name))
                {
                    var bindingExpr = BindingOperations.GetBindingExpression(element, dp);
                    if (bindingExpr != null)
                    {
                        var binding = bindingExpr.ParentBinding;

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

            type = type.BaseType;
        }

        return bindings;
    }

}
