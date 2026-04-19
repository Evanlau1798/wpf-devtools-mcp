using System.Buffers;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to trace routed events in WPF application
/// </summary>
public sealed class TraceRoutedEventsTool : PipeConnectedToolBase
{
    private static readonly TimeSpan CleanupFailedFollowUpGracePeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ReplayTimestampFallbackGracePeriod = TimeSpan.FromSeconds(1);

    private enum TraceNavigationSyncResult
    {
        NoChange,
        ClearedMatchingActiveTrace,
        StateSynchronized
    }

    public TraceRoutedEventsTool(SessionManager sessionManager) : base(sessionManager) { }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var mode = NormalizeMode(ParseStringParam(arguments, "mode"));
        if (mode == null)
        {
            return CreateInvalidParamError("mode must be one of: capture, start, get");
        }

        var eventName = ParseStringParam(arguments, "eventName");
        var duration = ParseIntParam(arguments, "duration");
        var allowShortStartDuration = ParseBoolParam(arguments, "allowShortStartDuration") ?? false;

        if (duration is < 0)
        {
            return CreateInvalidParamError("duration must be non-negative");
        }

        if (mode != "get" && string.IsNullOrEmpty(eventName))
        {
            return CreateMissingParamError("eventName");
        }

        var response = await SendInspectorRequestWithoutPiggybackAsync(
            processId,
            "trace_routed_events",
            new { elementId, eventName, duration, mode, allowShortStartDuration },
            cancellationToken);
        _sessionManager.TryGetNavigationState(processId, out var preSyncState);
        var replayTraceSnapshot = mode == "get"
            ? preSyncState?.ActiveTrace
            : null;
        var syncResult = SynchronizeTraceNavigationState(processId, elementId, eventName, duration, mode, response);
        response = mode == "get"
            ? MergePendingReplayIntoTraceResult(
                processId,
                response,
                replayTraceSnapshot,
                allowPreSyncFallback: syncResult == TraceNavigationSyncResult.ClearedMatchingActiveTrace)
            : response;

        return response;
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "capture";
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "capture" => "capture",
            "start" => "start",
            "get" => "get",
            _ => null
        };
    }

    private TraceNavigationSyncResult SynchronizeTraceNavigationState(
        int processId,
        string? elementId,
        string? eventName,
        int? requestedDurationMs,
        string mode,
        object response)
    {
        var payload = JsonSerializer.SerializeToElement(response);
        if (!IsSuccess(payload))
        {
            return TraceNavigationSyncResult.NoChange;
        }

        var responseSessionId = GetOptionalString(payload, "sessionId");
        _sessionManager.TryGetNavigationState(processId, out var existingState);
        var existingTrace = existingState?.ActiveTrace;
        if (existingTrace is not null && existingTrace.HasExpired(DateTimeOffset.UtcNow))
        {
            var endedSessionId = existingTrace.FollowUpExpiresAtUtc.HasValue
                ? existingTrace.SessionId
                : null;
            _sessionManager.ClearActiveTraceState(processId, endedSessionId);
            if (existingState is not null)
            {
                existingState = existingState with
                {
                    ActiveTrace = null,
                    LastEndedTraceSessionId = endedSessionId
                };
            }

            existingTrace = null;
        }

        if (IsMissingSessionIdForSessionAwareTrace(existingTrace, responseSessionId, mode))
        {
            return TraceNavigationSyncResult.NoChange;
        }

        if (IsStaleTraceResponse(existingTrace, responseSessionId, mode))
        {
            return TraceNavigationSyncResult.NoChange;
        }

        var isFrozenFollowUpState = HasDiagnosticsReasonCode(payload, "cleanupFailed");
        var isTracing = IsTracing(payload);
        if (!isTracing && !isFrozenFollowUpState)
        {
            var clearedMatchingActiveTrace = existingTrace is not null;
            _sessionManager.ClearActiveTraceState(processId, responseSessionId);
            return clearedMatchingActiveTrace
                ? TraceNavigationSyncResult.ClearedMatchingActiveTrace
                : TraceNavigationSyncResult.NoChange;
        }

        var rehydrateFromGet = mode == "get"
            && existingTrace is null
            && CanRehydrateTraceStateFromGet(existingState, responseSessionId);

        if (mode == "get" && existingTrace is null && !rehydrateFromGet)
        {
            return TraceNavigationSyncResult.NoChange;
        }

        var topLevelActiveEventName = GetOptionalString(payload, "activeEventName");
        var topLevelResolvedElementId = GetOptionalString(payload, "resolvedElementId");
        var resolvedEventName = mode == "get"
            ? topLevelActiveEventName
                ?? GetDiagnosticsString(payload, "activeEventName")
                ?? existingTrace?.EventName
                ?? GetOptionalString(payload, "eventName")
            : eventName
                ?? GetOptionalString(payload, "eventName")
                ?? existingTrace?.EventName;
        if (string.IsNullOrWhiteSpace(resolvedEventName))
        {
            return TraceNavigationSyncResult.NoChange;
        }

        var followUpExpiresAtUtc = ResolveFollowUpExpiry(existingTrace, isFrozenFollowUpState);
        var resolvedDuration = ResolveTraceDuration(payload, requestedDurationMs, existingTrace);
        var resolvedElementId = mode == "get"
            ? topLevelResolvedElementId
                ?? GetDiagnosticsString(payload, "resolvedElementId")
                ?? existingTrace?.ElementId
            : elementId ?? existingTrace?.ElementId;
        var startedAtUtc = ResolveTraceStartedAtUtc(
            mode,
            payload,
            resolvedDuration,
            existingTrace,
            requestedDurationMs,
            isFrozenFollowUpState);

        _sessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                resolvedEventName,
                resolvedElementId,
                startedAtUtc,
                resolvedDuration,
                responseSessionId,
                isFrozenFollowUpState,
                followUpExpiresAtUtc));
            return TraceNavigationSyncResult.StateSynchronized;
    }

    private static bool IsSuccess(JsonElement payload) =>
        payload.TryGetProperty("success", out var success) && success.GetBoolean();

    private static bool IsTracing(JsonElement payload) =>
        payload.TryGetProperty("isTracing", out var isTracing) && isTracing.GetBoolean();

    private static TimeSpan GetEffectiveDuration(JsonElement payload) =>
        payload.TryGetProperty("effectiveDuration", out var durationProperty)
        && durationProperty.ValueKind == JsonValueKind.Number
        && durationProperty.TryGetInt32(out var milliseconds)
        && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : TimeSpan.Zero;

    private static TimeSpan ResolveTraceDuration(
        JsonElement payload,
        int? durationFallbackMs,
        ActiveTraceNavigationState? existingTrace)
    {
        var effectiveDuration = GetEffectiveDuration(payload);
        if (effectiveDuration > TimeSpan.Zero)
        {
            return effectiveDuration;
        }

        if (payload.TryGetProperty("duration", out var durationProperty)
            && durationProperty.ValueKind == JsonValueKind.Number
            && durationProperty.TryGetInt32(out var reportedDurationMs)
            && reportedDurationMs > 0)
        {
            return TimeSpan.FromMilliseconds(reportedDurationMs);
        }

        if (payload.TryGetProperty("effectiveDurationMs", out var effectiveDurationMsProperty)
            && effectiveDurationMsProperty.ValueKind == JsonValueKind.Number
            && effectiveDurationMsProperty.TryGetInt32(out var reportedEffectiveDurationMs)
            && reportedEffectiveDurationMs > 0)
        {
            return TimeSpan.FromMilliseconds(reportedEffectiveDurationMs);
        }

        if (durationFallbackMs is > 0)
        {
            return TimeSpan.FromMilliseconds(durationFallbackMs.Value);
        }

        return existingTrace?.EffectiveDuration ?? TimeSpan.Zero;
    }

    private static DateTimeOffset ResolveTraceStartedAtUtc(
        string mode,
        JsonElement payload,
        TimeSpan resolvedDuration,
        ActiveTraceNavigationState? existingTrace,
        int? requestedDurationMs,
        bool isFrozenFollowUpState)
    {
        if (mode == "get" && existingTrace is not null)
        {
            return existingTrace.StartedAtUtc;
        }

        if (payload.TryGetProperty("traceStartedAtUtc", out var traceStartedAtProperty)
            && traceStartedAtProperty.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(traceStartedAtProperty.GetString(), out var traceStartedAtUtc))
        {
            return traceStartedAtUtc;
        }

        if (mode == "capture" && IsTracing(payload))
        {
            if (resolvedDuration > TimeSpan.Zero)
            {
                return DateTimeOffset.UtcNow - resolvedDuration;
            }

            if (requestedDurationMs is > 0)
            {
                return DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(requestedDurationMs.Value);
            }
        }

        if (mode == "capture" && HasDiagnosticsReasonCode(payload, "cleanupFailed"))
        {
            if (resolvedDuration > TimeSpan.Zero)
            {
                return DateTimeOffset.UtcNow - resolvedDuration;
            }

            if (requestedDurationMs is > 0)
            {
                return DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(requestedDurationMs.Value);
            }
        }

        return DateTimeOffset.UtcNow;
    }

    private static string? GetOptionalString(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? GetDiagnosticsString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty("diagnostics", out var diagnostics)
            && diagnostics.ValueKind == JsonValueKind.Object
            && diagnostics.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool HasDiagnosticsReasonCode(JsonElement payload, string reasonCode)
    {
        return payload.TryGetProperty("diagnostics", out var diagnostics)
            && diagnostics.ValueKind == JsonValueKind.Object
            && diagnostics.TryGetProperty("reasonCode", out var reasonCodeProperty)
            && reasonCodeProperty.ValueKind == JsonValueKind.String
            && string.Equals(reasonCodeProperty.GetString(), reasonCode, StringComparison.Ordinal);
    }

    private static DateTimeOffset? ResolveFollowUpExpiry(
        ActiveTraceNavigationState? existingTrace,
        bool isFrozenFollowUpState)
    {
        if (!isFrozenFollowUpState)
        {
            return null;
        }

        return existingTrace?.FollowUpExpiresAtUtc
            ?? DateTimeOffset.UtcNow.Add(CleanupFailedFollowUpGracePeriod);
    }

    private static bool IsStaleTraceResponse(
        ActiveTraceNavigationState? existingTrace,
        string? responseSessionId,
        string mode)
    {
        return mode != "start"
            && existingTrace?.SessionId is not null
            && responseSessionId is not null
            && !string.Equals(existingTrace.SessionId, responseSessionId, StringComparison.Ordinal);
    }

    private static bool IsMissingSessionIdForSessionAwareTrace(
        ActiveTraceNavigationState? existingTrace,
        string? responseSessionId,
        string mode)
    {
        return mode != "start"
            && existingTrace?.SessionId is not null
            && string.IsNullOrWhiteSpace(responseSessionId);
    }

    private static bool CanRehydrateTraceStateFromGet(
        NavigationSessionState? existingState,
        string? responseSessionId)
    {
        return !string.IsNullOrWhiteSpace(responseSessionId)
            && !string.Equals(existingState?.LastEndedTraceSessionId, responseSessionId, StringComparison.Ordinal)
            && !(existingState?.RecentlyEndedTraceSessionIds?.Contains(responseSessionId, StringComparer.Ordinal) ?? false);
    }

    private object MergePendingReplayIntoTraceResult(
        int processId,
        object response,
        ActiveTraceNavigationState? replayTraceSnapshot,
        bool allowPreSyncFallback)
    {
        var payload = JsonSerializer.SerializeToElement(response);
        var responseSessionId = GetOptionalString(payload, "sessionId");
        if (!IsSuccess(payload)
            || GetEventCount(payload) > 0
            || HasNonMergeableDiagnostics(payload)
            || !_sessionManager.TryPeekPendingEventReplayMetadata(processId, out var replayPayload, out var replaySavedAtUtc))
        {
            return response;
        }

        var activeTrace = ResolveReplayTraceState(processId, responseSessionId, replayTraceSnapshot, allowPreSyncFallback);
        if (activeTrace is null)
        {
            return response;
        }

        var replayEvents = GetMatchingReplayEvents(replayPayload, activeTrace, replaySavedAtUtc);
        if (replayEvents.Count == 0)
        {
            return response;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        var keepDiagnostics = ShouldKeepDiagnosticsAfterReplayMerge(payload);

        foreach (var property in payload.EnumerateObject())
        {
            if (property.NameEquals("eventCount")
                || property.NameEquals("events")
                || (property.NameEquals("diagnostics") && !keepDiagnostics))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteNumber("eventCount", replayEvents.Count);
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        foreach (var replayEvent in replayEvents)
        {
            WriteReplayTraceEvent(writer, replayEvent);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static bool ShouldKeepDiagnosticsAfterReplayMerge(JsonElement payload)
    {
        return payload.TryGetProperty("diagnostics", out var diagnostics)
            && diagnostics.ValueKind == JsonValueKind.Object
            && diagnostics.TryGetProperty("reasonCode", out var reasonCode)
            && reasonCode.ValueKind == JsonValueKind.String
            && string.Equals(reasonCode.GetString(), "cleanupFailed", StringComparison.Ordinal);
    }

    private ActiveTraceNavigationState? ResolveReplayTraceState(
        int processId,
        string? responseSessionId,
        ActiveTraceNavigationState? replayTraceSnapshot,
        bool allowPreSyncFallback)
    {
        if (_sessionManager.TryGetNavigationState(processId, out var navigationState)
            && navigationState?.ActiveTrace is not null
            && !IsMissingSessionIdForSessionAwareTrace(navigationState.ActiveTrace, responseSessionId, "get")
            && !IsStaleTraceResponse(navigationState.ActiveTrace, responseSessionId, "get"))
        {
            return navigationState.ActiveTrace;
        }

        if (!allowPreSyncFallback
            || replayTraceSnapshot is null
            || IsMissingSessionIdForSessionAwareTrace(replayTraceSnapshot, responseSessionId, "get")
            || replayTraceSnapshot.HasExpired(DateTimeOffset.UtcNow)
            || IsStaleTraceResponse(replayTraceSnapshot, responseSessionId, "get"))
        {
            return null;
        }

        return replayTraceSnapshot;
    }

    private static int GetEventCount(JsonElement payload) =>
        payload.TryGetProperty("eventCount", out var eventCount)
        && eventCount.ValueKind == JsonValueKind.Number
        && eventCount.TryGetInt32(out var count)
            ? count
            : 0;

    private static bool HasNonMergeableDiagnostics(JsonElement payload) =>
        payload.TryGetProperty("diagnostics", out var diagnostics)
        && diagnostics.ValueKind == JsonValueKind.Object
        && ((diagnostics.TryGetProperty("reasonCode", out var reasonCode)
                && reasonCode.ValueKind == JsonValueKind.String
                && string.Equals(reasonCode.GetString(), "filterMismatch", StringComparison.Ordinal))
            || (diagnostics.TryGetProperty("requestedEventMismatch", out var requestedEventMismatch)
                && requestedEventMismatch.ValueKind == JsonValueKind.True));

    private static List<JsonElement> GetMatchingReplayEvents(
        JsonElement replayPayload,
        ActiveTraceNavigationState activeTrace,
        DateTimeOffset replaySavedAtUtc)
    {
        if (!replayPayload.TryGetProperty("pendingEvents", out var pendingEvents)
            || pendingEvents.ValueKind != JsonValueKind.Array)
        {
            return new List<JsonElement>();
        }

        var traceWindowEndUtc = activeTrace.EffectiveDuration > TimeSpan.Zero
            ? activeTrace.StartedAtUtc.Add(activeTrace.EffectiveDuration)
            : DateTimeOffset.MaxValue;
        var fallbackWindowEndUtc = traceWindowEndUtc == DateTimeOffset.MaxValue
            ? DateTimeOffset.MaxValue
            : traceWindowEndUtc.Add(ReplayTimestampFallbackGracePeriod);

        return pendingEvents.EnumerateArray()
            .Where(pendingEvent => MatchesActiveTrace(
                pendingEvent,
                activeTrace,
                replaySavedAtUtc,
                traceWindowEndUtc,
                fallbackWindowEndUtc))
            .Select(pendingEvent => pendingEvent.Clone())
            .ToList();
    }

    private static bool MatchesActiveTrace(
        JsonElement pendingEvent,
        ActiveTraceNavigationState activeTrace,
        DateTimeOffset replaySavedAtUtc,
        DateTimeOffset traceWindowEndUtc,
        DateTimeOffset fallbackWindowEndUtc)
    {
        if (!pendingEvent.TryGetProperty("eventType", out var eventType)
            || eventType.ValueKind != JsonValueKind.String
            || !string.Equals(eventType.GetString(), "RoutedEvent", StringComparison.Ordinal))
        {
            return false;
        }

        if (!pendingEvent.TryGetProperty("eventName", out var eventName)
            || eventName.ValueKind != JsonValueKind.String
            || !string.Equals(eventName.GetString(), activeTrace.EventName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(activeTrace.ElementId)
            && (!pendingEvent.TryGetProperty("elementId", out var elementId)
                || elementId.ValueKind != JsonValueKind.String
                || !string.Equals(elementId.GetString(), activeTrace.ElementId, StringComparison.Ordinal)))
        {
            return false;
        }

        if (pendingEvent.TryGetProperty("timestampUtc", out var timestampProperty)
            && timestampProperty.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(timestampProperty.GetString(), out var timestamp))
        {
            return IsWithinTraceWindow(timestamp, activeTrace.StartedAtUtc, traceWindowEndUtc);
        }

        return IsWithinTraceWindow(replaySavedAtUtc, activeTrace.StartedAtUtc, fallbackWindowEndUtc);
    }

    private static bool IsWithinTraceWindow(
        DateTimeOffset candidate,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc) =>
        candidate >= windowStartUtc && candidate <= windowEndUtc;

    private static void WriteReplayTraceEvent(Utf8JsonWriter writer, JsonElement pendingEvent)
    {
        writer.WriteStartObject();
        writer.WriteString("timestamp", GetOptionalString(pendingEvent, "timestampUtc"));
        writer.WriteString("sender", GetOptionalString(pendingEvent, "senderType"));
        writer.WriteString("senderName", GetOptionalString(pendingEvent, "senderName"));
        writer.WriteString("eventName", GetOptionalString(pendingEvent, "eventName"));
        writer.WriteString("routingStrategy", GetOptionalString(pendingEvent, "routingStrategy"));

        if (pendingEvent.TryGetProperty("handled", out var handled))
        {
            writer.WritePropertyName("handled");
            handled.WriteTo(writer);
        }

        writer.WriteString("originalSource", GetOptionalString(pendingEvent, "originalSourceType"));
        writer.WriteEndObject();
    }
}
