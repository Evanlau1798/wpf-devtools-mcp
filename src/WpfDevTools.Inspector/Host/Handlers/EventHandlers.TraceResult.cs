using System.Text.Json;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Host.Handlers;

public partial class EventHandlers
{
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
            var totalEventCount = GetOptionalInt(tracePayload, "totalEventCount");
            var returnedEventCount = GetOptionalInt(tracePayload, "returnedEventCount");
            var eventsTruncated = GetOptionalBool(tracePayload, "eventsTruncated");
            var maxEvents = GetNullableInt(tracePayload, "maxEvents");
            var events = tracePayload.GetProperty("events");
            var handlerInvocationCount = tracePayload.TryGetProperty("handlerInvocationCount", out var hic)
                ? hic.GetInt32() : 0;
            var activeEventName = GetOptionalString(tracePayload, "activeEventName");
            var resolvedElementId = GetOptionalString(tracePayload, "resolvedElementId");
            var resolvedElementType = GetOptionalString(tracePayload, "resolvedElementType");
            var traceStartedAtUtc = GetOptionalString(tracePayload, "traceStartedAtUtc");
            var effectiveDurationMs = GetOptionalInt(tracePayload, "effectiveDurationMs");
            var registrationCount = GetOptionalInt(tracePayload, "registrationCount");
            var cleanupFailed = GetOptionalBool(tracePayload, "cleanupFailed");
            var cleanupIncomplete = GetOptionalBool(tracePayload, "cleanupIncomplete");
            var cleanupState = GetOptionalString(tracePayload, "cleanupState");
            var cleanupFailureMessage = GetOptionalString(tracePayload, "cleanupFailureMessage");
            var cleanupFailureType = GetOptionalString(tracePayload, "cleanupFailureType");

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
                        totalEventCount,
                        returnedEventCount,
                        eventsTruncated,
                        maxEvents,
                        events,
                        handlerInvocationCount,
                        activeEventName,
                        resolvedElementId,
                        resolvedElementType,
                        traceStartedAtUtc,
                        effectiveDurationMs,
                        registrationCount,
                        cleanupFailed,
                        cleanupIncomplete,
                        cleanupState,
                        cleanupFailureMessage,
                        cleanupFailureType,
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
                    totalEventCount,
                    returnedEventCount,
                    eventsTruncated,
                    maxEvents,
                    events,
                    handlerInvocationCount,
                    activeEventName,
                    resolvedElementId,
                    resolvedElementType,
                    traceStartedAtUtc,
                    effectiveDurationMs,
                    registrationCount,
                    cleanupFailed,
                    cleanupIncomplete,
                    cleanupState,
                    cleanupFailureMessage,
                    cleanupFailureType
                };
            }

            return new
            {
                success = true,
                sessionId,
                mode,
                isTracing,
                eventCount,
                totalEventCount,
                returnedEventCount,
                eventsTruncated,
                maxEvents,
                events,
                handlerInvocationCount,
                activeEventName,
                resolvedElementId,
                resolvedElementType,
                traceStartedAtUtc,
                effectiveDurationMs,
                registrationCount,
                cleanupFailed,
                cleanupIncomplete,
                cleanupState,
                cleanupFailureMessage,
                cleanupFailureType,
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
                requestedEventMismatch,
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

    private static int? GetNullableInt(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
                ? value
                : null;
    }

    private static bool GetOptionalBool(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.True;
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
