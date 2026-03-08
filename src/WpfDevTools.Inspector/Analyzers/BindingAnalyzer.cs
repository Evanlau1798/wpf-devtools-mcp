using System.Windows;
using System.Windows.Data;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Bindings
/// </summary>
public sealed partial class BindingAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    internal BindingAnalyzer() : this(new ElementFinder())
    {
    }

    /// <summary>
    /// Create a new BindingAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public BindingAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get all bindings for an element
    /// </summary>
    /// <param name="elementId">Element ID to get bindings for. If null, uses root element.</param>
    /// <returns>Result object containing success status and list of bindings</returns>
    public object GetBindings(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

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
    /// <param name="clearAfterRead">If true, clears error list after reading</param>
    /// <returns>Result object containing success status, error count, and list of binding errors</returns>
    public object GetBindingErrors(bool clearAfterRead = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var liveErrors = GetLiveBindingErrors();

            // Ensure trace listener is installed
            BindingErrorTraceListener.Install();

            IReadOnlyList<BindingErrorInfo> errors = BindingErrorTraceListener.Instance.GetErrors();
            if (errors.Count == 0)
            {
                errors = liveErrors;
            }

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
    /// <param name="elementId">Element ID to get DataContext chain for. If null, uses root element.</param>
    /// <returns>Result object containing success status and DataContext chain from element to root</returns>
    public object GetDataContextChain(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

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
    /// <param name="element">DependencyObject to analyze</param>
    /// <param name="propertyName">Name of property to get binding chain for</param>
    /// <returns>Result object containing binding resolution chain details</returns>
    public object GetBindingValueChain(DependencyObject element, string propertyName)
    {
        return InvokeOnUIThread<object>(() => GetBindingValueChainCore(element, propertyName));
    }

    /// <summary>
    /// Force binding to update source or target
    /// </summary>
    /// <param name="element">DependencyObject containing the binding</param>
    /// <param name="propertyName">Name of property with binding to update</param>
    /// <param name="direction">Update direction: "source" or "target"</param>
    /// <returns>Result object containing success status and update details</returns>
    public object ForceBindingUpdate(DependencyObject element, string propertyName, string direction)
    {
        return InvokeOnUIThread<object>(() => ForceBindingUpdateCore(element, propertyName, direction));
    }

    /// <summary>
    /// Get binding value chain by elementId (resolves element on UI thread)
    /// </summary>
    /// <param name="elementId">Element ID to analyze. If null, uses root element.</param>
    /// <param name="propertyName">Name of property to get binding chain for</param>
    /// <returns>Result object containing binding resolution chain details</returns>
    public object GetBindingValueChain(string? elementId, string propertyName)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

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
    /// <param name="elementId">Element ID containing the binding. If null, uses root element.</param>
    /// <param name="propertyName">Name of property with binding to update</param>
    /// <param name="direction">Update direction: "source" or "target"</param>
    /// <returns>Result object containing success status and update details</returns>
    public object ForceBindingUpdate(string? elementId, string propertyName, string direction)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

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
    /// <param name="element">DependencyObject to analyze</param>
    /// <param name="propertyName">Name of property to get binding chain for</param>
    /// <returns>Result object containing binding resolution chain details</returns>
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
            chain.Add(new Dictionary<string, object?>
            {
                ["step"] = "Binding",
                ["path"] = binding.Path?.Path,
                ["mode"] = binding.Mode.ToString(),
                ["updateSourceTrigger"] = binding.UpdateSourceTrigger.ToString(),
                ["converter"] = binding.Converter?.GetType().Name
            });
        }

        // Get data context chain
        var current = element as FrameworkElement;
        while (current != null)
        {
            if (current.DataContext != null)
            {
                chain.Add(new Dictionary<string, object?>
                {
                    ["step"] = "DataContext",
                    ["elementType"] = current.GetType().Name,
                    ["dataContextType"] = current.DataContext.GetType().Name,
                    ["dataContextValue"] = current.DataContext.ToString()
                });
                break;
            }
            current = current.Parent as FrameworkElement;
        }

        // Get resolved value
        var resolvedValue = bindingExpr.ResolvedSource;
        if (resolvedValue != null)
        {
            chain.Add(new Dictionary<string, object?>
            {
                ["step"] = "ResolvedSource",
                ["sourceType"] = resolvedValue.GetType().Name,
                ["sourceValue"] = resolvedValue.ToString()
            });
        }

        // Get final value
        var finalValue = element.GetValue(dp);
        chain.Add(new Dictionary<string, object?>
        {
            ["step"] = "FinalValue",
            ["valueType"] = finalValue?.GetType().Name,
            ["value"] = finalValue?.ToString()
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
    /// <param name="element">DependencyObject containing the binding</param>
    /// <param name="propertyName">Name of property with binding to update</param>
    /// <param name="direction">Update direction: "source" or "target"</param>
    /// <returns>Result object containing success status and update details</returns>
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

    private DependencyObject? ResolveElement(string? elementId)
    {
        return elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);
    }

    private List<object> GetDependencyPropertiesWithBindings(DependencyObject element)
    {
        var bindings = new List<object>();
        var seenProperties = new HashSet<string>();

        // Use LocalValueEnumerator to find all locally set properties (including attached properties)
        var enumerator = element.GetLocalValueEnumerator();
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            var dp = entry.Property;

            if (dp != null && seenProperties.Add(dp.Name))
            {
                // Try to get binding expression for this property
                var bindingExpr = BindingOperations.GetBindingExpression(element, dp);
                if (bindingExpr != null)
                {
                    var binding = bindingExpr.ParentBinding;

                    bindings.Add(new Dictionary<string, object?>
                    {
                        ["propertyName"] = dp.Name,
                        ["path"] = binding?.Path?.Path,
                        ["mode"] = binding?.Mode.ToString(),
                        ["updateSourceTrigger"] = binding?.UpdateSourceTrigger.ToString()
                    });
                }
            }
        }

        return bindings;
    }

}

