using System.Text.Json;
using System.Windows;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles RoutedEvent related requests
/// </summary>
public class EventHandlers : IRequestHandler
{
    private readonly EventAnalyzer _eventAnalyzer;

    public EventHandlers(EventAnalyzer eventAnalyzer)
    {
        _eventAnalyzer = eventAnalyzer;
    }

    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "trace_routed_events",
            "get_event_handlers",
            "fire_routed_event"
        };
    }

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "trace_routed_events" => await HandleTraceRoutedEventsAsync(@params, cancellationToken),
            "get_event_handlers" => await HandleGetEventHandlersAsync(@params, cancellationToken),
            "fire_routed_event" => await HandleFireRoutedEventAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleTraceRoutedEventsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var eventName = ParameterHelpers.GetStringParam(@params, "eventName");
        var duration = ParameterHelpers.GetIntParam(@params, "duration") ?? InspectorConstants.Defaults.EventTraceDuration;

        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentException("Missing required parameter: eventName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _eventAnalyzer.TraceRoutedEvents(elementId, eventName!, duration)));
    }

    private async Task<object> HandleGetEventHandlersAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var eventName = ParameterHelpers.GetStringParam(@params, "eventName");

        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentException("Missing required parameter: eventName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _eventAnalyzer.GetEventHandlers(elementId, eventName!)));
    }

    private async Task<object> HandleFireRoutedEventAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var eventName = ParameterHelpers.GetStringParam(@params, "eventName");
        var eventArgs = ParameterHelpers.GetObjectParam<object>(@params, "eventArgs");

        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentException("Missing required parameter: eventName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _eventAnalyzer.FireRoutedEvent(elementId, eventName!, eventArgs)));
    }
}
