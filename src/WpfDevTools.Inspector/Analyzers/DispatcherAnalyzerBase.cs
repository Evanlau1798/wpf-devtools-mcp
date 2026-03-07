using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;
using System.Text.Json;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Base class for analyzers that need to execute on the UI thread
/// </summary>
public abstract class DispatcherAnalyzerBase
{
    /// <summary>
    /// Execute an action on the UI thread with optional timeout
    /// </summary>
    protected T InvokeOnUIThread<T>(Func<T> action, TimeSpan? timeout = null)
    {
        // If no WPF application context, execute directly
        if (Application.Current == null)
        {
            return action();
        }

        // If already on UI thread, execute directly
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return action();
        }

        // Otherwise, invoke on UI thread with timeout
        var actualTimeout = timeout ?? InspectorConfig.UIThreadTimeout;
        return Application.Current.Dispatcher.Invoke(
            action,
            DispatcherPriority.Normal,
            CancellationToken.None,
            actualTimeout);
    }

    /// <summary>
    /// Execute an action on the UI thread (void return) with optional timeout
    /// </summary>
    protected void InvokeOnUIThread(Action action, TimeSpan? timeout = null)
    {
        // If no WPF application context, execute directly
        if (Application.Current == null)
        {
            action();
            return;
        }

        // If already on UI thread, execute directly
        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            var actualTimeout = timeout ?? InspectorConfig.UIThreadTimeout;
            Application.Current.Dispatcher.Invoke(
                action,
                DispatcherPriority.Normal,
                CancellationToken.None,
                actualTimeout);
        }
    }

    /// <summary>
    /// Check if we're already on the UI thread
    /// </summary>
    protected bool IsOnUIThread()
    {
        return Application.Current?.Dispatcher.CheckAccess() ?? false;
    }

    /// <summary>
    /// Convert a value to the specified target type using TypeConverter fallback.
    /// Shared utility used by analyzers that need to set DependencyProperty or ViewModel values.
    /// </summary>
    protected static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null) return null;

        value = value is JsonElement jsonElement
            ? ConvertJsonElement(jsonElement, targetType)
            : value;

        if (value == null) return null;
        if (targetType.IsAssignableFrom(value.GetType())) return value;

        // Try TypeConverter first (handles WPF types like Brush, Thickness, etc.)
        var converter = TypeDescriptor.GetConverter(targetType);
        if (converter.CanConvertFrom(value.GetType()))
        {
            return converter.ConvertFrom(value);
        }

        // Fallback to Convert.ChangeType for simple types
        return Convert.ChangeType(value, targetType);
    }

    private static object? ConvertJsonElement(JsonElement value, Type targetType)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
            JsonValueKind.Number => DeserializeJsonNumber(value, targetType),
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Deserialize(value.GetRawText(), targetType),
            _ => value.GetRawText()
        };
    }

    private static object DeserializeJsonNumber(JsonElement value, Type targetType)
    {
        try
        {
            return JsonSerializer.Deserialize(value.GetRawText(), targetType) ?? value.GetDouble();
        }
        catch (JsonException)
        {
            return value.GetDouble();
        }
    }

    /// <summary>
    /// Find a DependencyProperty by name on the given element's type hierarchy.
    /// Searches for a static field named "{propertyName}Property" through base types.
    /// </summary>
    protected static DependencyProperty? FindDependencyProperty(DependencyObject element, string propertyName)
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
