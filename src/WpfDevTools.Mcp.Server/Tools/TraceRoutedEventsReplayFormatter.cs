using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class TraceRoutedEventsReplayFormatter
{
    private static readonly TimeSpan ReplayTimestampFallbackGracePeriod = TimeSpan.FromSeconds(1);

    internal static List<JsonElement> GetMatchingReplayEvents(
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

    internal static void WriteReplayTraceEvent(Utf8JsonWriter writer, JsonElement pendingEvent)
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
            var fallbackWindowStartUtc = activeTrace.StartedAtUtc.Subtract(ReplayTimestampFallbackGracePeriod);
            return IsWithinTraceWindow(timestamp, activeTrace.StartedAtUtc, traceWindowEndUtc)
                || (timestamp >= fallbackWindowStartUtc
                    && timestamp < activeTrace.StartedAtUtc
                    && IsWithinTraceWindow(replaySavedAtUtc, activeTrace.StartedAtUtc, fallbackWindowEndUtc));
        }

        return IsWithinTraceWindow(replaySavedAtUtc, activeTrace.StartedAtUtc, fallbackWindowEndUtc);
    }

    private static bool IsWithinTraceWindow(
        DateTimeOffset candidate,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc) =>
        candidate >= windowStartUtc && candidate <= windowEndUtc;

    private static string? GetOptionalString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}