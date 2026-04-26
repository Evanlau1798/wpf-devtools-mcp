using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Globalization;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.ErrorHandling;

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
        var dispatcher = ResolveCurrentUIThreadDispatcher();
        return dispatcher == null
            ? action()
            : InvokeOnDispatcher(dispatcher, action, timeout);
    }

    /// <summary>
    /// Execute an action on the UI thread (void return) with optional timeout
    /// </summary>
    protected void InvokeOnUIThread(Action action, TimeSpan? timeout = null)
    {
        var dispatcher = ResolveCurrentUIThreadDispatcher();
        if (dispatcher == null)
        {
            action();
            return;
        }

        InvokeOnDispatcher(dispatcher, action, timeout);
    }

    /// <summary>
    /// Check if we're already on the UI thread
    /// </summary>
    protected bool IsOnUIThread()
    {
        return ResolveCurrentUIThreadDispatcher()?.CheckAccess() ?? false;
    }

    private static Dispatcher? ResolveCurrentUIThreadDispatcher()
    {
        return Application.Current?.Dispatcher
            ?? Dispatcher.FromThread(Thread.CurrentThread);
    }

    /// <summary>
    /// Execute an action on the specified dispatcher with optional timeout.
    /// Returns a structured unavailable result for object-returning analyzer calls.
    /// </summary>
    protected T InvokeOnDispatcher<T>(Dispatcher? dispatcher, Func<T> action, TimeSpan? timeout = null)
    {
        if (TryGetDispatcherUnavailableMessage(dispatcher, out var unavailableMessage))
        {
            return CreateDispatcherUnavailableResult<T>(unavailableMessage);
        }

        var targetDispatcher = dispatcher!;
        bool hasAccess;
        try
        {
            hasAccess = targetDispatcher.CheckAccess();
        }
        catch (InvalidOperationException ex)
        {
            return CreateDispatcherUnavailableResult<T>(
                "WPF dispatcher unavailable because access could not be validated; analyzer action was not executed.",
                ex);
        }

        if (hasAccess)
        {
            return action();
        }

        var actualTimeout = timeout ?? InspectorConfig.UIThreadTimeout;
        try
        {
            return targetDispatcher.Invoke(
                action,
                DispatcherPriority.Normal,
                CancellationToken.None,
                actualTimeout);
        }
        catch (InvalidOperationException ex) when (IsDispatcherShuttingDown(targetDispatcher))
        {
            return CreateDispatcherUnavailableResult<T>(
                "WPF dispatcher unavailable because it is shutting down; analyzer action was not executed.",
                ex);
        }
    }

    /// <summary>
    /// Execute a void action on the specified dispatcher with optional timeout.
    /// Throws a clear dispatcher unavailable error when the action cannot be marshalled safely.
    /// </summary>
    protected void InvokeOnDispatcher(Dispatcher? dispatcher, Action action, TimeSpan? timeout = null)
    {
        if (TryGetDispatcherUnavailableMessage(dispatcher, out var unavailableMessage))
        {
            throw CreateDispatcherUnavailableException(unavailableMessage);
        }

        var targetDispatcher = dispatcher!;
        bool hasAccess;
        try
        {
            hasAccess = targetDispatcher.CheckAccess();
        }
        catch (InvalidOperationException ex)
        {
            throw CreateDispatcherUnavailableException(
                "WPF dispatcher unavailable because access could not be validated; analyzer action was not executed.",
                ex);
        }

        if (hasAccess)
        {
            action();
            return;
        }

        var actualTimeout = timeout ?? InspectorConfig.UIThreadTimeout;
        try
        {
            targetDispatcher.Invoke(
                action,
                DispatcherPriority.Normal,
                CancellationToken.None,
                actualTimeout);
        }
        catch (InvalidOperationException ex) when (IsDispatcherShuttingDown(targetDispatcher))
        {
            throw CreateDispatcherUnavailableException(
                "WPF dispatcher unavailable because it is shutting down; analyzer action was not executed.",
                ex);
        }
    }

    private static bool TryGetDispatcherUnavailableMessage(
        Dispatcher? dispatcher,
        out string message)
    {
        if (dispatcher == null)
        {
            message = "WPF dispatcher unavailable; analyzer action was not executed because running WPF object access on the pipe thread is unsafe.";
            return true;
        }

        if (dispatcher.HasShutdownFinished)
        {
            message = "WPF dispatcher unavailable because shutdown has finished; analyzer action was not executed.";
            return true;
        }

        if (dispatcher.HasShutdownStarted)
        {
            message = "WPF dispatcher unavailable because shutdown has started; analyzer action was not executed.";
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static bool IsDispatcherShuttingDown(Dispatcher dispatcher)
    {
        return dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished;
    }

    private static T CreateDispatcherUnavailableResult<T>(string message, Exception? innerException = null)
    {
        var exception = CreateDispatcherUnavailableException(message, innerException);
        var errorPayload = ToolErrorFactory.OperationFailed(
            "access WPF dispatcher",
            exception,
            "Retry after the target UI dispatcher is available, or reconnect to the process before accessing WPF objects.",
            new { dispatcherUnavailable = true });

        if (typeof(T).IsAssignableFrom(typeof(ToolErrorPayload)))
        {
            return (T)(object)errorPayload;
        }

        if (TryCreateWrappedToolErrorResult(errorPayload, out T wrappedResult))
        {
            return wrappedResult;
        }

        throw exception;
    }

    private static bool TryCreateWrappedToolErrorResult<T>(ToolErrorPayload payload, out T result)
    {
        foreach (var constructor in typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 0
                || !parameters[0].ParameterType.IsAssignableFrom(typeof(ToolErrorPayload))
                || parameters.Skip(1).Any(static parameter => !CanAcceptNull(parameter.ParameterType)))
            {
                continue;
            }

            var arguments = new object?[parameters.Length];
            arguments[0] = payload;
            result = (T)constructor.Invoke(arguments);
            return true;
        }

        result = default!;
        return false;
    }

    private static bool CanAcceptNull(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }

    private static InvalidOperationException CreateDispatcherUnavailableException(
        string message,
        Exception? innerException = null)
    {
        return innerException == null
            ? new InvalidOperationException(message)
            : new InvalidOperationException(message, innerException);
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

        // Strip surrounding double-quotes from string values (handles JSON double-quoting by AI agents)
        if (value is string strVal)
        {
            value = NormalizeStringValue(strVal);
        }

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

    private static string NormalizeStringValue(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
        {
            return value.Substring(1, value.Length - 2);
        }
        return value;
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
    /// Format runtime values into stable, JSON-friendly strings for tool responses.
    /// </summary>
    protected static string? FormatResponseValue(object? value)
    {
        return value switch
        {
            null => null,
            double number when double.IsNaN(number) => "NaN",
            double number when double.IsPositiveInfinity(number) => "Infinity",
            double number when double.IsNegativeInfinity(number) => "-Infinity",
            float number when float.IsNaN(number) => "NaN",
            float number when float.IsPositiveInfinity(number) => "Infinity",
            float number when float.IsNegativeInfinity(number) => "-Infinity",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
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
