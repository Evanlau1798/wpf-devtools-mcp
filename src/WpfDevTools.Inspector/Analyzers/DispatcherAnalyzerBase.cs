using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;
using System.Reflection;
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
        return InvokeOnDispatcher(Application.Current?.Dispatcher, action, timeout);
    }

    /// <summary>
    /// Execute an action on the UI thread (void return) with optional timeout
    /// </summary>
    protected void InvokeOnUIThread(Action action, TimeSpan? timeout = null)
    {
        InvokeOnDispatcher(Application.Current?.Dispatcher, action, timeout);
    }

    /// <summary>
    /// Check if we're already on the UI thread
    /// </summary>
    protected bool IsOnUIThread()
    {
        return Application.Current?.Dispatcher.CheckAccess() ?? false;
    }

    /// <summary>
    /// Execute an action on the specified dispatcher with optional timeout.
    /// Falls back to direct execution when dispatcher is unavailable.
    /// </summary>
    protected T InvokeOnDispatcher<T>(Dispatcher? dispatcher, Func<T> action, TimeSpan? timeout = null)
    {
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return action();
        }

        var actualTimeout = timeout ?? InspectorConfig.UIThreadTimeout;
        return dispatcher.Invoke(
            action,
            DispatcherPriority.Normal,
            CancellationToken.None,
            actualTimeout);
    }

    /// <summary>
    /// Execute a void action on the specified dispatcher with optional timeout.
    /// Falls back to direct execution when dispatcher is unavailable.
    /// </summary>
    protected void InvokeOnDispatcher(Dispatcher? dispatcher, Action action, TimeSpan? timeout = null)
    {
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        var actualTimeout = timeout ?? InspectorConfig.UIThreadTimeout;
        dispatcher.Invoke(
            action,
            DispatcherPriority.Normal,
            CancellationToken.None,
            actualTimeout);
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
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var qualifiedProperty = TryFindQualifiedDependencyProperty(propertyName);
        if (qualifiedProperty != null)
        {
            return qualifiedProperty;
        }

        var simplePropertyName = GetSimplePropertyName(propertyName);
        var hierarchyProperty = FindDependencyPropertyOnHierarchy(element.GetType(), simplePropertyName);
        return hierarchyProperty ?? FindAttachedDependencyProperty(simplePropertyName);
    }

    private static DependencyProperty? FindDependencyPropertyOnHierarchy(Type? type, string propertyName)
    {
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

    private static DependencyProperty? TryFindQualifiedDependencyProperty(string propertyName)
    {
        var separatorIndex = propertyName.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == propertyName.Length - 1)
        {
            return null;
        }

        var ownerTypeName = propertyName.Substring(0, separatorIndex);
        var simplePropertyName = propertyName.Substring(separatorIndex + 1);
        var fieldName = simplePropertyName + "Property";

        foreach (var type in EnumerateLoadedTypes())
        {
            if (!IsMatchingOwnerType(type, ownerTypeName))
            {
                continue;
            }

            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field?.FieldType == typeof(DependencyProperty))
            {
                return field.GetValue(null) as DependencyProperty;
            }
        }

        return null;
    }

    private static DependencyProperty? FindAttachedDependencyProperty(string propertyName)
    {
        var fieldName = propertyName + "Property";
        DependencyProperty? match = null;

        foreach (var type in EnumerateLoadedTypes())
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field?.FieldType != typeof(DependencyProperty) || !LooksLikeAttachedPropertyOwner(type, propertyName))
            {
                continue;
            }

            var candidate = field.GetValue(null) as DependencyProperty;
            if (candidate == null)
            {
                continue;
            }

            if (match != null && !ReferenceEquals(match, candidate))
            {
                return null;
            }

            match = candidate;
        }

        return match;
    }

    private static string GetSimplePropertyName(string propertyName)
    {
        var separatorIndex = propertyName.LastIndexOf('.');
        return separatorIndex >= 0 ? propertyName.Substring(separatorIndex + 1) : propertyName;
    }

    private static bool IsMatchingOwnerType(Type type, string ownerTypeName)
    {
        return string.Equals(type.Name, ownerTypeName, StringComparison.Ordinal)
            || string.Equals(type.FullName, ownerTypeName, StringComparison.Ordinal)
            || string.Equals(type.FullName, $"{type.Namespace}.{ownerTypeName}", StringComparison.Ordinal);
    }

    private static bool LooksLikeAttachedPropertyOwner(Type type, string propertyName)
    {
        var getter = type.GetMethod($"Get{propertyName}", BindingFlags.Public | BindingFlags.Static);
        var setter = type.GetMethod($"Set{propertyName}", BindingFlags.Public | BindingFlags.Static);
        return getter != null || setter != null;
    }

    private static IEnumerable<Type> EnumerateLoadedTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(static t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                yield return type;
            }
        }
    }
}
