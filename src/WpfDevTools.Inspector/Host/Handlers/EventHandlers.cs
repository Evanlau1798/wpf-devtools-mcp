using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles RoutedEvent related requests.
/// </summary>
public class EventHandlers : IRequestHandler
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
        if (mode == "get")
        {
            var traceResult = _eventAnalyzer.GetEventTrace(eventName);
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
                    _eventAnalyzer.GetEventTrace(traceSession, eventName),
                    eventName,
                    actualDuration,
                    diagnostics: CreateCleanupFailedDiagnostics(cleanupFailure));
            }

            return CreateTraceSnapshotResult("capture", _eventAnalyzer.GetEventTrace(traceSession, eventName), eventName, actualDuration);
        }
        catch (OperationCanceledException)
        {
            if (traceSession != null
                && !_eventAnalyzer.CleanupTraceSession(traceSession, out var cleanupException))
            {
                return CreateTraceSnapshotResult(
                    "capture",
                    _eventAnalyzer.GetEventTrace(traceSession, eventName),
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
            var sessionId = tracePayload.TryGetProperty("sessionId", out var sessionIdProperty)
                && sessionIdProperty.ValueKind == JsonValueKind.String
                    ? sessionIdProperty.GetString()
                    : null;
            var isTracing = tracePayload.GetProperty("isTracing").GetBoolean();
            var eventCount = tracePayload.GetProperty("eventCount").GetInt32();
            var events = tracePayload.GetProperty("events");
            var handlerInvocationCount = tracePayload.TryGetProperty("handlerInvocationCount", out var hic)
                ? hic.GetInt32() : 0;
            var activeEventName = GetOptionalString(tracePayload, "activeEventName");
            var resolvedElementId = GetOptionalString(tracePayload, "resolvedElementId");
            var resolvedElementType = GetOptionalString(tracePayload, "resolvedElementType");
            var traceStartedAtUtc = GetOptionalString(tracePayload, "traceStartedAtUtc");
            var effectiveDurationMs = GetOptionalInt(tracePayload, "effectiveDurationMs");
            var registrationCount = GetOptionalInt(tracePayload, "registrationCount");

            if (mode == "capture")
            {
                if (diagnostics != null)
                {
                    return new
                    {
                        success = true,
                        sessionId,
                        mode,
                        eventName,
                        duration,
                        isTracing,
                        eventCount,
                        events,
                        handlerInvocationCount,
                        activeEventName,
                        resolvedElementId,
                        resolvedElementType,
                        traceStartedAtUtc,
                        effectiveDurationMs,
                        registrationCount,
                        diagnostics
                    };
                }

                return new
                {
                    success = true,
                    sessionId,
                    mode,
                    eventName,
                    duration,
                    isTracing,
                    eventCount,
                    events,
                    handlerInvocationCount,
                    activeEventName,
                    resolvedElementId,
                    resolvedElementType,
                    traceStartedAtUtc,
                    effectiveDurationMs,
                    registrationCount
                };
            }

            return new
            {
                success = true,
                sessionId,
                mode,
                isTracing,
                eventCount,
                events,
                handlerInvocationCount,
                activeEventName,
                resolvedElementId,
                resolvedElementType,
                traceStartedAtUtc,
                effectiveDurationMs,
                registrationCount,
                diagnostics
            };
        }

        return traceResult;
    }

    private static object? BuildZeroEventDiagnostics(
        object traceResult,
        string? requestedEventName)
    {
        var tracePayload = JsonSerializer.SerializeToElement(traceResult);
        if (!tracePayload.TryGetProperty("success", out var successProperty) || !successProperty.GetBoolean())
        {
            return null;
        }

        var activeEventName = GetOptionalString(tracePayload, "activeEventName");
        var resolvedElementId = GetOptionalString(tracePayload, "resolvedElementId");
        var resolvedElementType = GetOptionalString(tracePayload, "resolvedElementType");
        var startedAtUtc = tracePayload.TryGetProperty("traceStartedAtUtc", out var startedAtProperty)
            && startedAtProperty.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(startedAtProperty.GetString(), out var parsedStartedAtUtc)
                ? parsedStartedAtUtc
                : (DateTimeOffset?)null;
        var effectiveDurationMs = tracePayload.TryGetProperty("effectiveDurationMs", out var effectiveDurationProperty)
            && effectiveDurationProperty.ValueKind == JsonValueKind.Number
            && effectiveDurationProperty.TryGetInt32(out var parsedEffectiveDurationMs)
                ? parsedEffectiveDurationMs
                : 0;
        var registrationCount = tracePayload.TryGetProperty("registrationCount", out var registrationCountProperty)
            && registrationCountProperty.ValueKind == JsonValueKind.Number
            && registrationCountProperty.TryGetInt32(out var parsedRegistrationCount)
                ? parsedRegistrationCount
                : 0;

        var cleanupFailed = tracePayload.TryGetProperty("cleanupFailed", out var cleanupFailedProperty)
            && cleanupFailedProperty.GetBoolean();
        var cleanupFailureMessage = tracePayload.TryGetProperty("cleanupFailureMessage", out var cleanupFailureMessageProperty)
            ? cleanupFailureMessageProperty.GetString()
            : null;
        var cleanupFailureType = tracePayload.TryGetProperty("cleanupFailureType", out var cleanupFailureTypeProperty)
            ? cleanupFailureTypeProperty.GetString()
            : null;

        var eventCount = tracePayload.TryGetProperty("eventCount", out var eventCountProperty)
            ? eventCountProperty.GetInt32()
            : 0;

        var isTracing = tracePayload.TryGetProperty("isTracing", out var isTracingProperty)
            && isTracingProperty.GetBoolean();
        var getRequestedAtUtc = DateTimeOffset.UtcNow;
        var windowEndedAtUtc = startedAtUtc?.AddMilliseconds(effectiveDurationMs);
        var windowExpiredBeforeGet = windowEndedAtUtc.HasValue
            && getRequestedAtUtc > windowEndedAtUtc.Value;
        var expiredByMs = windowExpiredBeforeGet
            ? Math.Max(1, (int)Math.Round((getRequestedAtUtc - windowEndedAtUtc!.Value).TotalMilliseconds))
            : 0;
        var requestedEventMismatch = !string.IsNullOrWhiteSpace(requestedEventName)
            && !string.IsNullOrWhiteSpace(activeEventName)
            && !string.Equals(requestedEventName, activeEventName, StringComparison.OrdinalIgnoreCase);

        if (cleanupFailed)
        {
            return new
            {
                reasonCode = "cleanupFailed",
                message = cleanupFailureMessage ?? "Trace cleanup failed after the capture window ended.",
                cleanupFailureType,
                activeEventName,
                requestedEventName = requestedEventMismatch ? requestedEventName : null,
                requestedEventMismatch,
                registrationCount,
                resolvedElementId,
                resolvedElementType,
                windowExpiredBeforeGet,
                windowEndedAtUtc,
                getRequestedAtUtc,
                expiredByMs,
                suggestedAction = "Wait for the target UI thread to recover, then restart tracing or reconnect before requesting another capture."
            };
        }

        if (requestedEventMismatch)
        {
            return new
            {
                reasonCode = "filterMismatch",
                message = $"Requested event '{requestedEventName}' does not match active trace event '{activeEventName}'.",
                requestedEventName,
                activeEventName,
                windowExpiredBeforeGet,
                windowEndedAtUtc,
                getRequestedAtUtc,
                expiredByMs,
                suggestedAction = "Use the same eventName as the active trace session, or restart tracing with the new eventName."
            };
        }

        if (eventCount > 0)
        {
            return null;
        }

        if (isTracing && !windowExpiredBeforeGet)
        {
            var elapsedMs = startedAtUtc is null
                ? 0
                : Math.Max(
                    0,
                    (int)Math.Round((DateTimeOffset.UtcNow - startedAtUtc.Value).TotalMilliseconds));
            var remainingWindowMs = effectiveDurationMs > 0
                ? Math.Max(0, effectiveDurationMs - elapsedMs)
                : 0;

            return new
            {
                reasonCode = "captureWindowTooShort",
                message = "Tracing is still active and no events have been captured yet.",
                activeEventName,
                registrationCount,
                resolvedElementId,
                resolvedElementType,
                elapsedMs,
                effectiveDurationMs,
                remainingWindowMs,
                windowExpiredBeforeGet = false,
                windowEndedAtUtc,
                getRequestedAtUtc,
                expiredByMs = 0,
                suggestedAction = "Trigger the interaction while tracing remains active, then call trace_routed_events(mode='get') again."
            };
        }

        return new
        {
            reasonCode = "eventNotRaised",
            message = "Trace window ended without captured routed events.",
            activeEventName,
            registrationCount,
            resolvedElementId,
            resolvedElementType,
            windowExpiredBeforeGet,
            windowEndedAtUtc,
            getRequestedAtUtc,
            expiredByMs,
            suggestedAction = string.IsNullOrWhiteSpace(activeEventName)
                ? "Start tracing with trace_routed_events(mode='start', eventName=...) before retrieving results."
                : "Restart tracing and trigger the target interaction inside the capture window before calling mode='get'."
        };
    }

    private static string? GetOptionalString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static int GetOptionalInt(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
                ? value
                : 0;
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

    private static object CreateTraceCleanupFailureResult(Exception? cleanupException)
    {
        return ToolErrorFactory.OperationFailed(
            "stop routed event trace",
            cleanupException ?? new InvalidOperationException("Routed event trace cleanup failed."),
            "Wait for the target UI thread to become responsive, then retry routed event tracing.");
    }

    private static object CreateCleanupFailedDiagnostics(Exception? cleanupException)
    {
        return new
        {
            reasonCode = "cleanupFailed",
            message = cleanupException?.Message ?? "Trace cleanup failed after capture completed.",
            cleanupFailureType = cleanupException?.GetType().Name,
            suggestedAction = "Use get mode to inspect the frozen trace state, then retry after the target UI thread becomes responsive."
        };
    }

    private static object CreateCanceledCleanupFailedDiagnostics(Exception? cleanupException)
    {
        return new
        {
            reasonCode = "cleanupFailed",
            message = cleanupException?.Message ?? "Trace cleanup failed after capture cancellation.",
            cleanupFailureType = cleanupException?.GetType().Name,
            suggestedAction = "The capture request was canceled, but the trace is frozen. Use get mode to inspect the frozen trace state before retrying."
        };
    }
}
