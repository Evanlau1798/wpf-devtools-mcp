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
        CancellationTokenSource tokenSource)
    {
        Registrations = registrations;
        TokenSource = tokenSource;
    }

    public List<HandlerRegistration> Registrations { get; }

    public CancellationTokenSource TokenSource { get; }
}

/// <summary>
/// Utility methods for discovering available routed events on WPF element types
/// </summary>
internal static class RoutedEventDiscovery
{
    /// <summary>
    /// Enumerate all available RoutedEvent names for a given element type,
    /// walking up the inheritance hierarchy
    /// </summary>
    public static List<string> EnumerateAvailableRoutedEvents(Type elementType)
    {
        var events = new List<string>();
        var type = elementType;
        while (type != null && type != typeof(object))
        {
            foreach (var field in type.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.FieldType == typeof(RoutedEvent) && field.Name.EndsWith("Event"))
                {
                    events.Add(field.Name.Substring(0, field.Name.Length - "Event".Length));
                }
            }
            type = type.BaseType;
        }
        return events.Distinct().OrderBy(e => e).ToList();
    }
}
