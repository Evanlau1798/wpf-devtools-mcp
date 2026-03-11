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
    /// <param name="recursive">When true, also collect bindings from all descendant elements.</param>
    /// <param name="statusFilter">Optional status category filter: All, Active, or Error.</param>
    /// <returns>Result object containing success status and list of bindings</returns>
    public object GetBindings(string? elementId = null, bool recursive = false, string? statusFilter = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var bindings = recursive
                ? CollectBindingsRecursive(element)
                : GetDependencyPropertiesWithBindings(element);

            var filteredBindings = ApplyStatusFilter(bindings, statusFilter);
            if (filteredBindings is not null)
            {
                return filteredBindings;
            }

            return new { success = true, bindings };
        });
    }

    /// <summary>
    /// Get binding errors captured by PresentationTraceSources.
    /// Installs the trace listener if not already installed.
    /// </summary>
    /// <param name="maxErrors">Optional maximum number of binding errors to return after filtering.</param>
    /// <param name="sinceTimestamp">Optional ISO-8601 timestamp filter.</param>
    /// <param name="clearAfterRead">If true, clears error list after reading</param>
    /// <returns>Result object containing success status, error count, and list of binding errors</returns>
    public object GetBindingErrors(int? maxErrors = null, string? sinceTimestamp = null, bool clearAfterRead = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (maxErrors is <= 0)
            {
                return ToolErrorFactory.InvalidArgument(
                    "maxErrors must be a positive integer when provided",
                    "Provide maxErrors > 0 or omit it to return the full filtered error list.");
            }

            DateTime? sinceUtc = null;
            if (!string.IsNullOrWhiteSpace(sinceTimestamp))
            {
                if (!DateTimeOffset.TryParse(sinceTimestamp, out var parsed))
                {
                    return ToolErrorFactory.InvalidArgument(
                        "sinceTimestamp must be a valid ISO-8601 timestamp",
                        "Use an ISO-8601 UTC timestamp such as 2026-03-11T12:00:00Z.");
                }

                sinceUtc = parsed.UtcDateTime;
            }

            var liveErrors = GetLiveBindingErrors();

            // Ensure trace listener is installed
            BindingErrorTraceListener.Install();

            IReadOnlyList<BindingErrorInfo> errors = BindingErrorTraceListener.Instance.GetErrors();
            if (errors.Count == 0)
            {
                errors = liveErrors;
            }

            var filteredErrors = FilterOutValidationErrors(errors);
            if (sinceUtc.HasValue)
            {
                filteredErrors = filteredErrors
                    .Where(error => error.Timestamp >= sinceUtc.Value)
                    .ToList();
            }

            if (maxErrors.HasValue && filteredErrors.Count > maxErrors.Value)
            {
                filteredErrors = filteredErrors
                    .OrderBy(error => error.Timestamp)
                    .TakeLast(maxErrors.Value)
                    .ToList();
            }

            var result = new
            {
                success = true,
                errorCount = filteredErrors.Count,
                errors = filteredErrors.Select(e => new
                {
                    diagnosticKind = "BindingError",
                    sourceKind = e.Origin,
                    severity = "Error",
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
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var chain = new List<object>();

            // Walk up the tree collecting DataContext
            if (element is FrameworkElement fe)
            {
                var current = fe;
                while (current != null)
                {
                    var dataContext = current.DataContext;
                    var hasLocalValue = current.ReadLocalValue(FrameworkElement.DataContextProperty) != DependencyProperty.UnsetValue;
                    chain.Add(new
                    {
                        diagnosticKind = "DataContextScope",
                        elementId = _elementFinder.GenerateElementId(current),
                        elementType = current.GetType().Name,
                        elementName = current.Name,
                        dataContextType = dataContext?.GetType().Name,
                        hasDataContext = dataContext != null,
                        sourceKind = dataContext == null
                            ? "None"
                            : hasLocalValue ? "LocalDataContext" : "InheritedDataContext",
                        isInherited = dataContext != null && !hasLocalValue
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
                return ToolErrorFactory.ElementNotFound(elementId);
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
                return ToolErrorFactory.ElementNotFound(elementId);
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
            return ToolErrorFactory.ElementNotFound();
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            return ToolErrorFactory.InvalidArgument(
                "propertyName is required",
                "Provide propertyName to inspect a specific bound DependencyProperty.");
        }

        // Find DependencyProperty
        var dp = FindDependencyProperty(element, propertyName);
        if (dp == null)
        {
            return ToolErrorFactory.PropertyNotFound(propertyName, element.GetType().Name);
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
                ["diagnosticKind"] = "BindingValueResolutionStep",
                ["sourceKind"] = "BindingDefinition",
                ["step"] = "Binding",
                ["path"] = binding.Path?.Path,
                ["mode"] = binding.Mode.ToString(),
                ["updateSourceTrigger"] = binding.UpdateSourceTrigger.ToString(),
                ["converter"] = binding.Converter?.GetType().Name,
                ["fallbackValue"] = binding.FallbackValue?.ToString()
            });
        }

        if (element is FrameworkElement frameworkElement)
        {
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
                ["dataContextValue"] = localDataContext?.ToString()
            });

            var current = frameworkElement.Parent as FrameworkElement;
            while (current != null)
            {
                if (current.DataContext != null)
                {
                    chain.Add(new Dictionary<string, object?>
                    {
                        ["diagnosticKind"] = "BindingValueResolutionStep",
                        ["sourceKind"] = "InheritedDataContext",
                        ["step"] = "InheritedDataContext",
                        ["elementType"] = current.GetType().Name,
                        ["dataContextType"] = current.DataContext.GetType().Name,
                        ["dataContextValue"] = current.DataContext.ToString()
                    });
                    break;
                }

                current = current.Parent as FrameworkElement;
            }
        }

        // Get resolved value
        var resolvedValue = bindingExpr.ResolvedSource;
        if (resolvedValue != null)
        {
            chain.Add(new Dictionary<string, object?>
            {
                ["diagnosticKind"] = "BindingValueResolutionStep",
                ["sourceKind"] = "ResolvedSource",
                ["step"] = "ResolvedSource",
                ["sourceType"] = resolvedValue.GetType().Name,
                ["sourceValue"] = resolvedValue.ToString(),
                ["resolutionState"] = "Resolved"
            });
        }
        else
        {
            chain.Add(new Dictionary<string, object?>
            {
                ["diagnosticKind"] = "BindingValueResolutionStep",
                ["sourceKind"] = "ResolvedSource",
                ["step"] = "ResolvedSource",
                ["sourceType"] = null,
                ["sourceValue"] = null,
                ["resolutionState"] = "Unresolved"
            });
        }

        // Get final value
        var finalValue = element.GetValue(dp);
        chain.Add(new Dictionary<string, object?>
        {
            ["diagnosticKind"] = "BindingValueResolutionStep",
            ["sourceKind"] = "FinalValue",
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
            return ToolErrorFactory.ElementNotFound();
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            return ToolErrorFactory.InvalidArgument(
                "propertyName is required",
                "Provide propertyName to update a specific binding.");
        }

        // Find DependencyProperty
        var dp = FindDependencyProperty(element, propertyName);
        if (dp == null)
        {
            return ToolErrorFactory.PropertyNotFound(propertyName, element.GetType().Name);
        }

        // Get binding expression
        var bindingExpr = BindingOperations.GetBindingExpression(element, dp);
        if (bindingExpr == null)
        {
            return ToolErrorFactory.InvalidArgument(
                "No binding on this property",
                "Call get_bindings first to confirm the target property has an active binding.");
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
                return ToolErrorFactory.InvalidArgument(
                    "Invalid direction. Use 'Source' or 'Target'",
                    "Set direction to 'Source' or 'Target' when calling force_binding_update.");
            }
        }
        catch (Exception ex)
        {
            return ToolErrorFactory.InvalidArgument(
                $"Failed to update binding: {ex.Message}",
                "Inspect the binding with get_bindings or get_binding_value_chain before retrying.");
        }
    }

}
