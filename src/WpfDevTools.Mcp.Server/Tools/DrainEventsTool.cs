using System.Text.Json;
using System.Buffers;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to drain shared runtime watch events from the Inspector.
/// </summary>
public sealed class DrainEventsTool : PipeConnectedToolBase
{
    public DrainEventsTool(SessionManager sessionManager) : base(sessionManager)
    {
    }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var maxEvents = ParseIntParam(arguments, "maxEvents");
        if (maxEvents is <= 0)
        {
            return CreateInvalidParamError("maxEvents must be a positive integer when provided");
        }

        var sinceTimestamp = ParseStringParam(arguments, "sinceTimestamp");
        if (sinceTimestamp is not null && !DateTimeOffset.TryParse(sinceTimestamp, out _))
        {
            return CreateInvalidParamError("sinceTimestamp must be a valid ISO-8601 timestamp when provided");
        }

        _sessionManager.TryTakePendingEventReplay(processId, out var replayPayload);

        var liveResult = await SendInspectorRequestAsync(
            processId,
            "drain_events",
            new
            {
                maxEvents,
                eventTypes = ParseStringArrayParam(arguments, "eventTypes"),
                elementId,
                sinceTimestamp
            },
            cancellationToken).ConfigureAwait(false);

        if (replayPayload.ValueKind == JsonValueKind.Undefined)
        {
            return liveResult;
        }

        var livePayload = liveResult is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(liveResult);
        if (!IsSuccessfulPayload(livePayload))
        {
            return liveResult;
        }

        return MergeReplayPayload(
            replayPayload,
            livePayload,
            maxEvents,
            ParseStringArrayParam(arguments, "eventTypes"),
            elementId,
            sinceTimestamp is null ? null : DateTimeOffset.Parse(sinceTimestamp));
    }

    private static bool IsSuccessfulPayload(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("success", out var success)
        && success.ValueKind == JsonValueKind.True;

    private static object MergeReplayPayload(
        JsonElement replayPayload,
        JsonElement livePayload,
        int? maxEvents,
        string[]? eventTypes,
        string? elementId,
        DateTimeOffset? sinceTimestamp)
    {
        var maxCount = maxEvents ?? int.MaxValue;
        var eventTypeSet = eventTypes is { Length: > 0 }
            ? new HashSet<string>(eventTypes, StringComparer.Ordinal)
            : null;
        var mergedEvents = new List<JsonElement>();

        AppendMatchingEvents(mergedEvents, replayPayload, maxCount, eventTypeSet, elementId, sinceTimestamp);
        AppendMatchingEvents(mergedEvents, livePayload, maxCount, eventTypeSet, elementId, sinceTimestamp);

        var droppedEventCount = GetIntProperty(replayPayload, "droppedEventCount")
            + GetIntProperty(livePayload, "droppedEventCount");

        if (mergedEvents.Count == 0)
        {
            return new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount
            };
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteBoolean("success", true);
        writer.WriteNumber("pendingEventCount", mergedEvents.Count);
        writer.WriteNumber("droppedEventCount", droppedEventCount);
        writer.WritePropertyName("pendingEvents");
        writer.WriteStartArray();
        foreach (var pendingEvent in mergedEvents)
        {
            pendingEvent.WriteTo(writer);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void AppendMatchingEvents(
        List<JsonElement> target,
        JsonElement payload,
        int maxCount,
        HashSet<string>? eventTypes,
        string? elementId,
        DateTimeOffset? sinceTimestamp)
    {
        if (target.Count >= maxCount
            || !payload.TryGetProperty("pendingEvents", out var pendingEvents)
            || pendingEvents.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var pendingEvent in pendingEvents.EnumerateArray())
        {
            if (target.Count >= maxCount)
            {
                return;
            }

            if (!MatchesFilters(pendingEvent, eventTypes, elementId, sinceTimestamp))
            {
                continue;
            }

            target.Add(pendingEvent.Clone());
        }
    }

    private static bool MatchesFilters(
        JsonElement pendingEvent,
        HashSet<string>? eventTypes,
        string? elementId,
        DateTimeOffset? sinceTimestamp)
    {
        if (eventTypes != null
            && (!pendingEvent.TryGetProperty("eventType", out var eventType)
                || eventType.ValueKind != JsonValueKind.String
                || !eventTypes.Contains(eventType.GetString()!)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(elementId)
            && (!pendingEvent.TryGetProperty("elementId", out var pendingElementId)
                || pendingElementId.ValueKind != JsonValueKind.String
                || !string.Equals(pendingElementId.GetString(), elementId, StringComparison.Ordinal)))
        {
            return false;
        }

        if (sinceTimestamp.HasValue)
        {
            if (!pendingEvent.TryGetProperty("timestampUtc", out var timestampProperty)
                || timestampProperty.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(timestampProperty.GetString(), out var timestamp)
                || timestamp < sinceTimestamp.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetIntProperty(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
            ? value
            : 0;
}
