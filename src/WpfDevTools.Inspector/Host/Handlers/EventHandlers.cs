using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles RoutedEvent related requests.
/// </summary>
public partial class EventHandlers : IRequestHandler
{
    private readonly EventAnalyzer _eventAnalyzer;
    private readonly Func<Exception?>? _afterDrainCleanup;

    /// <summary>
    /// Create a new <see cref="EventHandlers"/> instance.
    /// </summary>
    /// <param name="eventAnalyzer">Event analyzer used for RoutedEvent operations.</param>
    /// <param name="afterDrainCleanup">Optional cleanup invoked after a successful drain_events readback.</param>
    public EventHandlers(EventAnalyzer eventAnalyzer, Func<Exception?>? afterDrainCleanup = null)
    {
        _eventAnalyzer = eventAnalyzer;
        _afterDrainCleanup = afterDrainCleanup;
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
        var maxEvents = ParameterHelpers.GetIntParam(@params, "maxEvents");
        if (maxEvents is <= 0)
        {
            throw new ArgumentException("Invalid parameter: maxEvents must be a positive integer when provided");
        }

        if (mode == "get")
        {
            var traceResult = _eventAnalyzer.GetEventTrace(eventName, maxEvents);
            var diagnostics = BuildZeroEventDiagnostics(traceResult, eventName);
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

        if (duration < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "duration must be non-negative");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var startOutcome = _eventAnalyzer.StartTraceRoutedEvents(
            elementId,
            eventName!,
            effectiveDuration,
            scheduleAutoStop: mode == "start");
        var startResult = startOutcome.Result;
        var traceSession = startOutcome.Session;

        var startPayload = JsonSerializer.SerializeToElement(startResult);
        if (startPayload.TryGetProperty("success", out var successProperty) && !successProperty.GetBoolean())
        {
            return startResult;
        }

        var actualDuration = traceSession?.Metadata.EffectiveDurationMs
            ?? (startPayload.TryGetProperty("duration", out var durationProperty)
                && durationProperty.ValueKind == JsonValueKind.Number
                && durationProperty.TryGetInt32(out var reportedDuration)
                    ? reportedDuration
                    : effectiveDuration);

        if (mode == "start")
        {
            return new
            {
                success = true,
                sessionId = traceSession?.Metadata.SessionId,
                mode = "start",
                eventName,
                requestedDuration = duration,
                effectiveDuration = actualDuration,
                shortDurationOverrideUsed,
                isTracing = true,
                message = shortDurationOverrideUsed
                    ? $"Started tracing '{eventName}' for {actualDuration}ms using explicit short-duration override"
                    : duration != actualDuration
                    ? $"Started tracing '{eventName}' for {actualDuration}ms (requested {duration}ms, adjusted to the supported trace window)"
                    : startPayload.TryGetProperty("message", out var messageProperty)
                        ? messageProperty.GetString()
                        : $"Started tracing '{eventName}' for {actualDuration}ms"
            };
        }

        try
        {
            await Task.Delay(actualDuration, cancellationToken).ConfigureAwait(false);

            if (traceSession != null
                && !_eventAnalyzer.CleanupTraceSession(traceSession, out var cleanupFailure))
            {
                return CreateTraceSnapshotResult(
                    "capture",
                    _eventAnalyzer.GetEventTrace(traceSession, eventName, maxEvents),
                    eventName,
                    actualDuration,
                    diagnostics: CreateCleanupFailedDiagnostics(cleanupFailure));
            }

            return CreateTraceSnapshotResult(
                "capture",
                _eventAnalyzer.GetEventTrace(traceSession, eventName, maxEvents),
                eventName,
                actualDuration);
        }
        catch (OperationCanceledException)
        {
            if (traceSession != null
                && !_eventAnalyzer.CleanupTraceSession(traceSession, out var cleanupException))
            {
                return CreateTraceSnapshotResult(
                    "capture",
                    _eventAnalyzer.GetEventTrace(traceSession, eventName, maxEvents),
                    eventName,
                    actualDuration,
                    diagnostics: CreateCanceledCleanupFailedDiagnostics(cleanupException));
            }

            throw;
        }
        catch
        {
            if (traceSession != null)
            {
                _eventAnalyzer.CleanupTraceSession(traceSession, out _);
            }

            throw;
        }
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

        var result = await Task.Run(
            () => _eventAnalyzer.DrainEvents(maxEvents, eventTypes, elementId, sinceTimestamp),
            cancellationToken).ConfigureAwait(false);

        if (IsSuccessfulPayload(result))
        {
            try
            {
                var cleanupFailure = _afterDrainCleanup?.Invoke();
                if (cleanupFailure != null)
                {
                    return AttachCleanupIncomplete(result, cleanupFailure);
                }
            }
            catch (Exception ex)
            {
                return AttachCleanupIncomplete(result, ex);
            }
        }

        return result;
    }

    private static bool IsSuccessfulPayload(object result)
    {
        var payload = JsonSerializer.SerializeToElement(result);
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.True;
    }

    private static object AttachCleanupIncomplete(object result, Exception exception)
    {
        var payload = JsonSerializer.SerializeToElement(result);
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        var response = new Dictionary<string, object?>();
        foreach (var property in payload.EnumerateObject())
        {
            response[property.Name] = property.Value.Clone();
        }

        response["cleanupIncomplete"] = true;
        response["cleanupFailureMessage"] = exception.Message;
        response["cleanupFailureType"] = exception.GetType().Name;
        return JsonSerializer.SerializeToElement(response);
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

}
