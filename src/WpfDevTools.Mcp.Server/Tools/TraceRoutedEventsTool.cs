using System.Buffers;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to trace routed events in WPF application
/// </summary>
public sealed class TraceRoutedEventsTool : PipeConnectedToolBase
{
    private static readonly TimeSpan ReplayMergeGracePeriod = TimeSpan.FromSeconds(30);

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

        if (mode != "get" && string.IsNullOrEmpty(eventName))
        {
            return CreateMissingParamError("eventName");
        }

        var response = await SendInspectorRequestAsync(
            processId,
            "trace_routed_events",
            new { elementId, eventName, duration, mode, allowShortStartDuration },
            cancellationToken);
        response = mode == "get"
            ? MergePendingReplayIntoTraceResult(processId, response)
            : response;

        SynchronizeTraceNavigationState(processId, elementId, eventName, mode, response);
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

    private void SynchronizeTraceNavigationState(
        int processId,
        string? elementId,
        string? eventName,
        string mode,
        object response)
    {
        var payload = JsonSerializer.SerializeToElement(response);
        if (!IsSuccess(payload))
        {
            return;
        }

        if (mode == "start" && IsTracing(payload))
        {
            _sessionManager.SetActiveTraceState(
                processId,
                new ActiveTraceNavigationState(
                    eventName ?? GetOptionalString(payload, "eventName") ?? string.Empty,
                    elementId,
                    DateTimeOffset.UtcNow,
                    GetEffectiveDuration(payload)));
            return;
        }

        if (mode == "get" && !IsTracing(payload))
        {
            _sessionManager.ClearActiveTraceState(processId);
        }
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

    private static string? GetOptionalString(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private object MergePendingReplayIntoTraceResult(int processId, object response)
    {
        var payload = JsonSerializer.SerializeToElement(response);
        if (!IsSuccess(payload)
            || GetEventCount(payload) > 0
            || !_sessionManager.TryGetNavigationState(processId, out var navigationState)
            || navigationState?.ActiveTrace is null
            || !_sessionManager.TryPeekPendingEventReplayMetadata(processId, out var replayPayload, out var replaySavedAtUtc))
        {
            return response;
        }

        var replayEvents = GetMatchingReplayEvents(replayPayload, navigationState.ActiveTrace, replaySavedAtUtc);
        if (replayEvents.Count == 0)
        {
            return response;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in payload.EnumerateObject())
        {
            if (property.NameEquals("eventCount")
                || property.NameEquals("events")
                || property.NameEquals("diagnostics"))
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

    private static int GetEventCount(JsonElement payload) =>
        payload.TryGetProperty("eventCount", out var eventCount)
        && eventCount.ValueKind == JsonValueKind.Number
        && eventCount.TryGetInt32(out var count)
            ? count
            : 0;

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

        var windowEndUtc = activeTrace.EffectiveDuration > TimeSpan.Zero
            ? activeTrace.StartedAtUtc.Add(activeTrace.EffectiveDuration).Add(ReplayMergeGracePeriod)
            : DateTimeOffset.MaxValue;

        return pendingEvents.EnumerateArray()
            .Where(pendingEvent => MatchesActiveTrace(pendingEvent, activeTrace, replaySavedAtUtc, windowEndUtc))
            .Select(pendingEvent => pendingEvent.Clone())
            .ToList();
    }

    private static bool MatchesActiveTrace(
        JsonElement pendingEvent,
        ActiveTraceNavigationState activeTrace,
        DateTimeOffset replaySavedAtUtc,
        DateTimeOffset windowEndUtc)
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

        if (!IsWithinTraceWindow(replaySavedAtUtc, activeTrace.StartedAtUtc, windowEndUtc))
        {
            return false;
        }

        if (!pendingEvent.TryGetProperty("timestampUtc", out var timestampProperty)
            || timestampProperty.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(timestampProperty.GetString(), out var timestamp))
        {
            return true;
        }

        return IsWithinTraceWindow(timestamp, activeTrace.StartedAtUtc, windowEndUtc)
            || IsWithinTraceWindow(replaySavedAtUtc, activeTrace.StartedAtUtc, windowEndUtc);
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
