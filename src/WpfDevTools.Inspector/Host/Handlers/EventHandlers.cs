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
            "fire_routed_event",
            "drain_events"
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
            "drain_events" => await HandleDrainEventsAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleTraceRoutedEventsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var mode = NormalizeTraceMode(ParameterHelpers.GetStringParam(@params, "mode"));
        var eventName = ParameterHelpers.GetStringParam(@params, "eventName");
        if (mode == "get")
        {
            var traceResult = _eventAnalyzer.GetEventTrace(eventName);
            var diagnostics = BuildZeroEventDiagnostics(
                traceResult,
                _eventAnalyzer.GetLatestTraceMetadata(),
                eventName);
            return CreateTraceSnapshotResult(mode, traceResult, diagnostics: diagnostics);
        }

        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var duration = ParameterHelpers.GetIntParam(@params, "duration") ?? InspectorConstants.Defaults.EventTraceDuration;
        var allowShortStartDuration = ParameterHelpers.GetBoolParam(@params, "allowShortStartDuration") ?? false;

        var shortDurationOverrideUsed = mode == "start"
            && allowShortStartDuration
            && duration < InspectorConstants.Defaults.StartModeMinDuration;

        var effectiveDuration = mode == "start"
            ? shortDurationOverrideUsed
                ? duration
                : Math.Max(duration, InspectorConstants.Defaults.StartModeMinDuration)
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
                shortDurationOverrideUsed,
                isTracing = true,
                message = shortDurationOverrideUsed
                    ? $"Started tracing '{eventName}' for {effectiveDuration}ms using explicit short-duration override"
                    : duration != effectiveDuration
                    ? $"Started tracing '{eventName}' for {effectiveDuration}ms (requested {duration}ms, enforced minimum {InspectorConstants.Defaults.StartModeMinDuration}ms for AI agent round-trips)"
                    : startPayload.TryGetProperty("message", out var messageProperty)
                        ? messageProperty.GetString()
                        : $"Started tracing '{eventName}' for {effectiveDuration}ms"
            };
        }

        await Task.Delay(effectiveDuration, cancellationToken).ConfigureAwait(false);
        return CreateTraceSnapshotResult("capture", _eventAnalyzer.GetEventTrace(eventName), eventName, effectiveDuration);
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

    private async Task<object> HandleDrainEventsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var maxEvents = ParameterHelpers.GetIntParam(@params, "maxEvents");
        if (maxEvents is <= 0)
        {
            throw new ArgumentException("Invalid parameter: maxEvents must be a positive integer when provided");
        }

        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var eventTypes = ParameterHelpers.GetStringArrayParam(@params, "eventTypes");
        var sinceTimestamp = ParseSinceTimestamp(@params);

        return await Task.Run(
            () => _eventAnalyzer.DrainEvents(maxEvents, eventTypes, elementId, sinceTimestamp),
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
        int? duration = null,
        object? diagnostics = null)
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
                handlerInvocationCount,
                diagnostics
            };
        }

        return traceResult;
    }

    private static object? BuildZeroEventDiagnostics(
        object traceResult,
        TraceSessionMetadata? traceMetadata,
        string? requestedEventName)
    {
        var tracePayload = JsonSerializer.SerializeToElement(traceResult);
        if (!tracePayload.TryGetProperty("success", out var successProperty) || !successProperty.GetBoolean())
        {
            return null;
        }

        var eventCount = tracePayload.TryGetProperty("eventCount", out var eventCountProperty)
            ? eventCountProperty.GetInt32()
            : 0;
        if (eventCount > 0)
        {
            return null;
        }

        var isTracing = tracePayload.TryGetProperty("isTracing", out var isTracingProperty)
            && isTracingProperty.GetBoolean();

        if (!string.IsNullOrWhiteSpace(requestedEventName)
            && traceMetadata is not null
            && !string.Equals(requestedEventName, traceMetadata.EventName, StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                reasonCode = "filterMismatch",
                message = $"Requested event '{requestedEventName}' does not match active trace event '{traceMetadata.EventName}'.",
                requestedEventName,
                activeEventName = traceMetadata.EventName,
                suggestedAction = "Use the same eventName as the active trace session, or restart tracing with the new eventName."
            };
        }

        if (isTracing)
        {
            var elapsedMs = traceMetadata is null
                ? 0
                : Math.Max(
                    0,
                    (int)Math.Round((DateTimeOffset.UtcNow - traceMetadata.StartedAtUtc).TotalMilliseconds));
            var effectiveDurationMs = traceMetadata?.EffectiveDurationMs ?? 0;
            var remainingWindowMs = effectiveDurationMs > 0
                ? Math.Max(0, effectiveDurationMs - elapsedMs)
                : 0;

            return new
            {
                reasonCode = "captureWindowTooShort",
                message = "Tracing is still active and no events have been captured yet.",
                activeEventName = traceMetadata?.EventName,
                registrationCount = traceMetadata?.RegistrationCount ?? 0,
                resolvedElementId = traceMetadata?.ElementId,
                resolvedElementType = traceMetadata?.ResolvedElementType,
                elapsedMs,
                effectiveDurationMs,
                remainingWindowMs,
                suggestedAction = "Trigger the interaction while tracing remains active, then call trace_routed_events(mode='get') again."
            };
        }

        return new
        {
            reasonCode = "eventNotRaised",
            message = "Trace window ended without captured routed events.",
            activeEventName = traceMetadata?.EventName,
            registrationCount = traceMetadata?.RegistrationCount ?? 0,
            resolvedElementId = traceMetadata?.ElementId,
            resolvedElementType = traceMetadata?.ResolvedElementType,
            suggestedAction = traceMetadata is null
                ? "Start tracing with trace_routed_events(mode='start', eventName=...) before retrieving results."
                : "Restart tracing and trigger the target interaction inside the capture window before calling mode='get'."
        };
    }

    private static DateTimeOffset? ParseSinceTimestamp(JsonElement? @params)
    {
        var sinceTimestamp = ParameterHelpers.GetStringParam(@params, "sinceTimestamp");
        if (sinceTimestamp is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(sinceTimestamp, out var parsed))
        {
            throw new ArgumentException("Invalid parameter: sinceTimestamp must be a valid ISO-8601 timestamp");
        }

        return parsed;
    }
}
