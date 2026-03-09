using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles RoutedEvent related requests.
/// </summary>
public class EventHandlers : IRequestHandler
{
    private readonly EventAnalyzer _eventAnalyzer;

    /// <summary>
    /// Create a new <see cref="EventHandlers"/> instance.
    /// </summary>
    /// <param name="eventAnalyzer">Event analyzer used for RoutedEvent operations.</param>
    public EventHandlers(EventAnalyzer eventAnalyzer)
    {
        _eventAnalyzer = eventAnalyzer;
    }

    /// <summary>
    /// Get list of supported method names.
    /// </summary>
    /// <returns>Enumerable of method names this handler supports.</returns>
    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "trace_routed_events",
            "get_event_handlers",
            "fire_routed_event"
        };
    }

    /// <summary>
    /// Handle an Inspector request.
    /// </summary>
    /// <param name="method">Method name to execute.</param>
    /// <param name="params">JSON parameters for the method.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result object from method execution.</returns>
    /// <exception cref="InvalidOperationException">Thrown when method is not supported.</exception>
    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "trace_routed_events" => await HandleTraceRoutedEventsAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_event_handlers" => await HandleGetEventHandlersAsync(@params, cancellationToken).ConfigureAwait(false),
            "fire_routed_event" => await HandleFireRoutedEventAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleTraceRoutedEventsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var mode = NormalizeTraceMode(ParameterHelpers.GetStringParam(@params, "mode"));
        if (mode == "get")
        {
            return CreateTraceSnapshotResult(mode, _eventAnalyzer.GetEventTrace());
        }

        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var eventName = ParameterHelpers.GetStringParam(@params, "eventName");
        var duration = ParameterHelpers.GetIntParam(@params, "duration") ?? InspectorConstants.Defaults.EventTraceDuration;

        // For "start" mode, enforce minimum duration to ensure AI agent has time for IPC round-trips
        var effectiveDuration = mode == "start"
            ? Math.Max(duration, InspectorConstants.Defaults.StartModeMinDuration)
            : duration;

        if (string.IsNullOrEmpty(eventName))
        {
            throw new ArgumentException("Missing required parameter: eventName");
        }

        var startResult = await Task.Run(
            () => _eventAnalyzer.TraceRoutedEvents(elementId, eventName!, effectiveDuration),
            cancellationToken).ConfigureAwait(false);

        var startPayload = JsonSerializer.SerializeToElement(startResult);
        if (startPayload.TryGetProperty("success", out var successProperty) && !successProperty.GetBoolean())
        {
            return startResult;
        }

        if (mode == "start")
        {
            return new
            {
                success = true,
                mode = "start",
                eventName,
                requestedDuration = duration,
                effectiveDuration,
                isTracing = true,
                message = duration != effectiveDuration
                    ? $"Started tracing '{eventName}' for {effectiveDuration}ms (requested {duration}ms, enforced minimum {InspectorConstants.Defaults.StartModeMinDuration}ms for AI agent round-trips)"
                    : startPayload.TryGetProperty("message", out var messageProperty)
                        ? messageProperty.GetString()
                        : $"Started tracing '{eventName}' for {effectiveDuration}ms"
            };
        }

        await Task.Delay(effectiveDuration, cancellationToken).ConfigureAwait(false);
        return CreateTraceSnapshotResult("capture", _eventAnalyzer.GetEventTrace(), eventName, effectiveDuration);
    }

    private async Task<object> HandleGetEventHandlersAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var eventName = ParameterHelpers.GetStringParam(@params, "eventName");

        if (string.IsNullOrEmpty(eventName))
        {
            throw new ArgumentException("Missing required parameter: eventName");
        }

        return await Task.Run(
            () => _eventAnalyzer.GetEventHandlers(elementId, eventName!),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleFireRoutedEventAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var eventName = ParameterHelpers.GetStringParam(@params, "eventName");
        var eventArgs = ParameterHelpers.GetObjectParam<object>(@params, "eventArgs");

        if (string.IsNullOrEmpty(eventName))
        {
            throw new ArgumentException("Missing required parameter: eventName");
        }

        return await Task.Run(
            () => _eventAnalyzer.FireRoutedEvent(elementId, eventName!, eventArgs),
            cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeTraceMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "capture";
        }

        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedMode switch
        {
            "capture" => "capture",
            "start" => "start",
            "get" => "get",
            _ => throw new ArgumentException("Invalid parameter: mode must be one of 'capture', 'start', or 'get'")
        };
    }

    private static object CreateTraceSnapshotResult(
        string mode,
        object traceResult,
        string? eventName = null,
        int? duration = null)
    {
        var tracePayload = JsonSerializer.SerializeToElement(traceResult);
        if (tracePayload.TryGetProperty("success", out var traceSuccess) && traceSuccess.GetBoolean())
        {
            var isTracing = tracePayload.GetProperty("isTracing").GetBoolean();
            var eventCount = tracePayload.GetProperty("eventCount").GetInt32();
            var events = tracePayload.GetProperty("events");
            var handlerInvocationCount = tracePayload.TryGetProperty("handlerInvocationCount", out var hic)
                ? hic.GetInt32() : 0;

            if (mode == "capture")
            {
                return new
                {
                    success = true,
                    mode,
                    eventName,
                    duration,
                    isTracing,
                    eventCount,
                    events,
                    handlerInvocationCount
                };
            }

            return new
            {
                success = true,
                mode,
                isTracing,
                eventCount,
                events,
                handlerInvocationCount
            };
        }

        return traceResult;
    }
}
