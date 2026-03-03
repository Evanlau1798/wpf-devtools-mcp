using System.Windows;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF DependencyProperty values and sources
/// </summary>
public class DependencyPropertyAnalyzer
{
    /// <summary>
    /// Get value source for a DependencyProperty
    /// </summary>
    public object GetValueSource(string propertyName, string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetValueSource(propertyName, elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

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
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetMetadata(propertyName, elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

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
            propertyName = propertyName,
            defaultValue = metadata.DefaultValue?.ToString(),
            hasCoerceValueCallback = metadata.CoerceValueCallback != null,
            hasPropertyChangedCallback = metadata.PropertyChangedCallback != null
        };
    }

    /// <summary>
    /// Set local value for a DependencyProperty
    /// </summary>
    public object SetValue(string propertyName, object value, string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => SetValue(propertyName, value, elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

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
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ClearValue(propertyName, elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

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

    private DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    private DependencyObject? FindElementById(string elementId)
    {
        // TODO: Implement element lookup by ID
        return GetRootElement();
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
}
