using System.Windows;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Represents a single handler registration for cleanup during trace session teardown
/// </summary>
internal sealed record HandlerRegistration(
    UIElement Element,
    RoutedEvent RoutedEvent,
    RoutedEventHandler Handler);

/// <summary>
/// Tracks active trace session state including all handler registrations
/// </summary>
internal sealed class ActiveTraceSession
{
    public ActiveTraceSession(
        List<HandlerRegistration> registrations,
        CancellationTokenSource tokenSource,
        TraceSessionMetadata metadata)
    {
        Registrations = registrations;
        TokenSource = tokenSource;
        Metadata = metadata;
    }

    public List<HandlerRegistration> Registrations { get; }

    public CancellationTokenSource TokenSource { get; }

    public TraceSessionMetadata Metadata { get; }
}

internal sealed record CompletedTraceSnapshot(
    IReadOnlyList<object> Events,
    int HandlerInvocationCount);

internal sealed record TraceCleanupFailure(
    string SessionId,
    string EventName,
    string ExceptionType,
    string Message);

internal sealed record TraceSessionMetadata(
    string SessionId,
    string EventName,
    string ElementId,
    DateTimeOffset StartedAtUtc,
    int EffectiveDurationMs,
    int RegistrationCount,
    string ResolvedElementType);

/// <summary>
/// Utility methods for discovering and resolving WPF RoutedEvents by name.
/// Searches type hierarchy first, then falls back to global EventManager
/// and WPF assembly scanning for events not in the element's hierarchy.
/// </summary>
internal static class RoutedEventDiscovery
{
    /// <summary>
    /// Find a RoutedEvent by name. Searches the type hierarchy first,
    /// then falls back to global EventManager and WPF assembly scanning.
    /// </summary>
    public static RoutedEvent? FindRoutedEvent(Type elementType, string eventName)
    {
        // 1. Search in element's type hierarchy (fastest path)
        var hierarchyResult = FindOnHierarchy(elementType, eventName);
        if (hierarchyResult != null)
        {
            return hierarchyResult;
        }

        // 2. Fallback: search globally for events not in this type's hierarchy
        //    e.g., tracing Click on a Window/Grid where ClickEvent is on ButtonBase
        return FindGlobal(eventName);
    }

    /// <summary>
    /// Enumerate all available RoutedEvent names for a given element type.
    /// Includes events from the type hierarchy AND globally registered events.
    /// </summary>
    public static List<string> EnumerateAvailableRoutedEvents(Type elementType)
    {
        var eventNames = new HashSet<string>(StringComparer.Ordinal);

        // 1. Walk the type hierarchy
        var type = elementType;
        while (type != null && type != typeof(object))
        {
            foreach (var field in type.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.FieldType == typeof(RoutedEvent) && field.Name.EndsWith("Event"))
                {
                    eventNames.Add(field.Name.Substring(0, field.Name.Length - "Event".Length));
                }
            }
            type = type.BaseType;
        }

        // 2. Include globally registered events
        var allEvents = EventManager.GetRoutedEvents();
        if (allEvents != null)
        {
            foreach (var routedEvent in allEvents)
            {
                eventNames.Add(routedEvent.Name);
            }
        }

        return eventNames.OrderBy(e => e).ToList();
    }

    private static RoutedEvent? FindOnHierarchy(Type? type, string eventName)
    {
        var fieldName = eventName + "Event";

        while (type != null && type != typeof(object))
        {
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);

            if (field != null && field.FieldType == typeof(RoutedEvent))
            {
                return field.GetValue(null) as RoutedEvent;
            }

            type = type.BaseType;
        }

        return null;
    }

    private static RoutedEvent? FindGlobal(string eventName)
    {
        // 1. Search registered events first (fast if class is already loaded)
        var allEvents = EventManager.GetRoutedEvents();
        if (allEvents != null)
        {
            foreach (var routedEvent in allEvents)
            {
                if (string.Equals(routedEvent.Name, eventName, StringComparison.Ordinal))
                {
                    return routedEvent;
                }
            }
        }

        // 2. Scan WPF assemblies for the static RoutedEvent field
        //    This handles cases where the owning type hasn't been loaded yet
        var fieldName = eventName + "Event";
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null ||
                (!assemblyName.StartsWith("PresentationFramework", StringComparison.Ordinal) &&
                 !assemblyName.StartsWith("PresentationCore", StringComparison.Ordinal) &&
                 !assemblyName.StartsWith("WindowsBase", StringComparison.Ordinal)))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(static t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                try
                {
                    var field = type.GetField(fieldName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.DeclaredOnly);

                    if (field != null && field.FieldType == typeof(RoutedEvent))
                    {
                        return field.GetValue(null) as RoutedEvent;
                    }
                }
                catch
                {
                    // Skip types that can't be inspected
                }
            }
        }

        return null;
    }
}
