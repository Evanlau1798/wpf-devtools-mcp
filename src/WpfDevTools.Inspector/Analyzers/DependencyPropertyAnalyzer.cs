using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF DependencyProperty values and sources
/// </summary>
public class DependencyPropertyAnalyzer
{
    private readonly ElementFinder _elementFinder;

    public DependencyPropertyAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get value source for a DependencyProperty
    /// </summary>
    public object GetValueSource(string propertyName, string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetValueSource(propertyName, elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not DependencyObject depObj)
        {
            return new { error = "Element is not a DependencyObject" };
        }

        // Find DependencyProperty by name
        var dp = FindDependencyProperty(depObj, propertyName);
        if (dp == null)
        {
            return new { error = $"DependencyProperty '{propertyName}' not found" };
        }

        // Get value source
        var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
        var effectiveValue = depObj.GetValue(dp);

        return new
        {
            propertyName = propertyName,
            baseValueSource = valueSource.BaseValueSource.ToString(),
            isExpression = valueSource.IsExpression,
            isAnimated = valueSource.IsAnimated,
            isCoerced = valueSource.IsCoerced,
            isCurrent = valueSource.IsCurrent,
            effectiveValue = effectiveValue?.ToString()
        };
    }

    /// <summary>
    /// Get metadata for a DependencyProperty
    /// </summary>
    public object GetMetadata(string propertyName, string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetMetadata(propertyName, elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not DependencyObject depObj)
        {
            return new { error = "Element is not a DependencyObject" };
        }

        // Find DependencyProperty by name
        var dp = FindDependencyProperty(depObj, propertyName);
        if (dp == null)
        {
            return new { error = $"DependencyProperty '{propertyName}' not found" };
        }

        // Get metadata
        var metadata = dp.GetMetadata(depObj.GetType());

        return new
        {
            success = true,
            propertyName,
            defaultValue = metadata.DefaultValue?.ToString(),
            hasCoerceValueCallback = metadata.CoerceValueCallback != null,
            hasPropertyChangedCallback = metadata.PropertyChangedCallback != null,
            isReadOnly = dp.ReadOnly,
            ownerType = dp.OwnerType.Name,
            propertyType = dp.PropertyType.Name,
            // Framework metadata (if available)
            affectsArrange = (metadata as FrameworkPropertyMetadata)?.AffectsArrange ?? false,
            affectsMeasure = (metadata as FrameworkPropertyMetadata)?.AffectsMeasure ?? false,
            affectsRender = (metadata as FrameworkPropertyMetadata)?.AffectsRender ?? false,
            inherits = (metadata as FrameworkPropertyMetadata)?.Inherits ?? false,
            isDataBindingAllowed = (metadata as FrameworkPropertyMetadata)?.IsDataBindingAllowed ?? true
        };
    }

    /// <summary>
    /// Set local value for a DependencyProperty
    /// </summary>
    public object SetValue(string propertyName, object value, string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => SetValue(propertyName, value, elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        if (element is not DependencyObject depObj)
        {
            return new { success = false, error = "Element is not a DependencyObject" };
        }

        // Find DependencyProperty by name
        var dp = FindDependencyProperty(depObj, propertyName);
        if (dp == null)
        {
            return new { success = false, error = $"DependencyProperty '{propertyName}' not found" };
        }

        try
        {
            // Convert value to correct type
            var targetType = dp.PropertyType;
            var convertedValue = Convert.ChangeType(value, targetType);

            depObj.SetValue(dp, convertedValue);
            return new { success = true, message = $"Property '{propertyName}' set successfully" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to set property: {ex.Message}" };
        }
    }

    /// <summary>
    /// Clear local value for a DependencyProperty
    /// </summary>
    public object ClearValue(string propertyName, string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ClearValue(propertyName, elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        if (element is not DependencyObject depObj)
        {
            return new { success = false, error = "Element is not a DependencyObject" };
        }

        // Find DependencyProperty by name
        var dp = FindDependencyProperty(depObj, propertyName);
        if (dp == null)
        {
            return new { success = false, error = $"DependencyProperty '{propertyName}' not found" };
        }

        try
        {
            depObj.ClearValue(dp);
            return new { success = true, message = $"Property '{propertyName}' cleared successfully" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to clear property: {ex.Message}" };
        }
    }

    private DependencyProperty? FindDependencyProperty(DependencyObject element, string propertyName)
    {
        var type = element.GetType();

        // Try to find static field with name ending in "Property"
        var fieldName = propertyName + "Property";
        var field = type.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (field != null && field.FieldType == typeof(DependencyProperty))
        {
            return field.GetValue(null) as DependencyProperty;
        }

        // Search in base types
        var baseType = type.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            field = baseType.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (field != null && field.FieldType == typeof(DependencyProperty))
            {
                return field.GetValue(null) as DependencyProperty;
            }
            baseType = baseType.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Start watching DependencyProperty changes
    /// </summary>
    public object WatchChanges(string propertyName, string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => WatchChanges(propertyName, elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        if (element is not DependencyObject depObj)
        {
            return new { success = false, error = "Element is not a DependencyObject" };
        }

        // Find DependencyProperty by name
        var dp = FindDependencyProperty(depObj, propertyName);
        if (dp == null)
        {
            return new { success = false, error = $"DependencyProperty '{propertyName}' not found" };
        }

        // TODO: Implement DependencyPropertyDescriptor.AddValueChanged
        // This requires event push mechanism to MCP Server
        return new { success = true, message = $"Watching property '{propertyName}' (not yet implemented)" };
    }

    /// <summary>
    /// Stop watching DependencyProperty changes
    /// </summary>
    public object UnwatchChanges(string propertyName, string? elementId = null)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => UnwatchChanges(propertyName, elementId));
        }

        // TODO: Implement DependencyPropertyDescriptor.RemoveValueChanged
        return new { success = true, message = $"Stopped watching property '{propertyName}' (not yet implemented)" };
    }
}
