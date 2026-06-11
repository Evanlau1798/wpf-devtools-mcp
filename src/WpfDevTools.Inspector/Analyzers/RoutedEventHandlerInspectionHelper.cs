using System.Windows;

namespace WpfDevTools.Inspector.Analyzers;

internal sealed record RoutedEventHandlerMetadata(
    string HandlerType,
    string? TargetType,
    string MethodName,
    bool InvokeHandledEventsToo);

internal static class RoutedEventHandlerInspectionHelper
{
    private const string EventHandlersStoreMember = "EventHandlersStore";
    private static bool? _reflectionSupported;
    private static readonly object ReflectionLock = new();

    internal static bool IsReflectionSupported()
    {
        lock (ReflectionLock)
        {
            if (_reflectionSupported.HasValue)
            {
                return _reflectionSupported.Value;
            }

            _reflectionSupported = GetEventHandlersStoreMember() != null;
            return _reflectionSupported.Value;
        }
    }

    internal static IReadOnlyList<RoutedEventHandlerMetadata> GetHandlerInfos(UIElement uiElement, RoutedEvent routedEvent)
    {
        var handlers = new List<RoutedEventHandlerMetadata>();
        var eventHandlersStore = GetEventHandlersStore(uiElement);

        if (eventHandlersStore == null)
        {
            return handlers;
        }

        var getRoutedEventHandlersMethod = eventHandlersStore.GetType().GetMethod(
            "GetRoutedEventHandlers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        if (getRoutedEventHandlersMethod == null)
        {
            return handlers;
        }

        var routedEventHandlers = getRoutedEventHandlersMethod.Invoke(
            eventHandlersStore,
            new object[] { routedEvent }) as RoutedEventHandlerInfo[];

        if (routedEventHandlers == null)
        {
            return handlers;
        }

        foreach (var handlerInfo in routedEventHandlers)
        {
            var handler = handlerInfo.Handler;
            handlers.Add(new RoutedEventHandlerMetadata(
                handler.GetType().Name,
                handler.Target?.GetType().Name,
                handler.Method.Name,
                handlerInfo.InvokeHandledEventsToo));
        }

        return handlers;
    }

    private static object? GetEventHandlersStore(UIElement element)
    {
        var member = GetEventHandlersStoreMember();

        return member switch
        {
            System.Reflection.FieldInfo field => field.GetValue(element),
            System.Reflection.PropertyInfo property => property.GetValue(element),
            _ => null
        };
    }

    private static System.Reflection.MemberInfo? GetEventHandlersStoreMember()
    {
        var field = typeof(UIElement).GetField(
            EventHandlersStoreMember,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (field != null)
        {
            return field;
        }

        return typeof(UIElement).GetProperty(
            EventHandlersStoreMember,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    }
}
