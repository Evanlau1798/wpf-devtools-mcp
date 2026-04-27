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

        if (!_sessionManager.TryGetSessionGeneration(processId, out var expectedSessionGeneration))
        {
            return CreateNotConnectedError(processId);
        }

        var eventTypes = NormalizeEventTypes(ParseStringArrayParam(arguments, "eventTypes"));
        var sinceTimestamp = ParseStringParam(arguments, "sinceTimestamp");
        if (sinceTimestamp is not null && !DateTimeOffset.TryParse(sinceTimestamp, out _))
        {
            return CreateInvalidParamError("sinceTimestamp must be a valid ISO-8601 timestamp when provided");
        }

        using var replayLock = await _sessionManager.AcquirePendingEventReplayLockAsync(processId, cancellationToken).ConfigureAwait(false);
        if (replayLock.SessionGeneration != expectedSessionGeneration)
        {
            return CreateNotConnectedError(processId);
        }

        _sessionManager.TryPeekPendingEventReplay(processId, replayLock.SessionGeneration, out var replayPayload);
        var liveDrainMaxEvents = replayPayload.ValueKind == JsonValueKind.Undefined
            ? maxEvents
            : int.MaxValue;

        var liveResult = await SendInspectorRequestAsync(
            processId,
            expectedSessionGeneration,
            "drain_events",
            new
            {
                maxEvents = liveDrainMaxEvents,
                eventTypes,
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
            return _sessionManager.TryPeekPendingEventReplay(processId, replayLock.SessionGeneration, out var preservedReplayPayload)
                ? CreateReplayPreservedFailurePayload(livePayload, preservedReplayPayload)
                : liveResult;
        }

        var replayMergeResult = MergeReplayPayload(
            replayPayload,
            livePayload,
            maxEvents,
            eventTypes,
            elementId,
            sinceTimestamp is null ? null : DateTimeOffset.Parse(sinceTimestamp));

        _sessionManager.TryTakePendingEventReplay(processId, replayLock.SessionGeneration, out _);
        if (replayMergeResult.RemainingReplayPayload.ValueKind != JsonValueKind.Undefined)
        {
            _sessionManager.SavePendingEventReplay(
                processId,
                replayLock.SessionGeneration,
                replayMergeResult.RemainingReplayPayload);
        }

        return replayMergeResult.ResponsePayload;
    }

    private static bool IsSuccessfulPayload(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("success", out var success)
        && success.ValueKind == JsonValueKind.True;

    private static string[]? NormalizeEventTypes(string[]? eventTypes)
    {
        if (eventTypes is not { Length: > 0 })
        {
            return null;
        }

        return eventTypes.Any(eventType =>
            string.Equals(eventType?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            ? null
            : eventTypes;
    }

    internal static (JsonElement ResponsePayload, JsonElement RemainingReplayPayload) MergeReplayPayloadForSharedBuffer(
        JsonElement replayPayload,
        JsonElement livePayload,
        int? maxEvents,
        string[]? eventTypes,
        string? elementId,
        DateTimeOffset? sinceTimestamp)
    {
        var result = MergeReplayPayload(replayPayload, livePayload, maxEvents, eventTypes, elementId, sinceTimestamp);
        return (result.ResponsePayload, result.RemainingReplayPayload);
    }

    private static ReplayMergeResult MergeReplayPayload(
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
        var remainingReplayEvents = new List<JsonElement>();
        var remainingLiveEvents = new List<JsonElement>();

        PartitionEvents(
            mergedEvents,
            remainingReplayEvents,
            replayPayload,
            maxCount,
            eventTypeSet,
            elementId,
            sinceTimestamp);
        PartitionEvents(
            mergedEvents,
            remainingLiveEvents,
            livePayload,
            maxCount,
            eventTypeSet,
            elementId,
            sinceTimestamp);

        var droppedEventCount = GetIntProperty(replayPayload, "droppedEventCount")
            + GetIntProperty(livePayload, "droppedEventCount");
        var cleanupIncomplete = HasCleanupIncompleteDiagnostics(livePayload)
            || HasCleanupIncompleteDiagnostics(replayPayload);
        var cleanupFailureMessage = GetCleanupFailureDetail(livePayload, "cleanupFailureMessage")
            ?? GetCleanupFailureDetail(replayPayload, "cleanupFailureMessage");
        var cleanupFailureType = GetCleanupFailureDetail(livePayload, "cleanupFailureType")
            ?? GetCleanupFailureDetail(replayPayload, "cleanupFailureType");

        var responsePayload = CreateDrainPayload(
            mergedEvents,
            droppedEventCount,
            cleanupIncomplete,
            cleanupFailureMessage,
            cleanupFailureType);
        remainingReplayEvents.AddRange(remainingLiveEvents);
        var remainingReplayPayload = remainingReplayEvents.Count > 0
            ? CreateDrainPayload(
                remainingReplayEvents,
                droppedEventCount: 0,
                cleanupIncomplete: false,
                cleanupFailureMessage: null,
                cleanupFailureType: null)
            : default;

        return new ReplayMergeResult(responsePayload, remainingReplayPayload);
    }

    private static JsonElement CreateDrainPayload(
        List<JsonElement> pendingEvents,
        int droppedEventCount,
        bool cleanupIncomplete,
        string? cleanupFailureMessage,
        string? cleanupFailureType)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteBoolean("success", true);
        writer.WriteNumber("pendingEventCount", pendingEvents.Count);
        writer.WriteNumber("droppedEventCount", droppedEventCount);
        if (cleanupIncomplete)
        {
            writer.WriteBoolean("cleanupIncomplete", true);
            if (cleanupFailureMessage != null)
            {
                writer.WriteString("cleanupFailureMessage", cleanupFailureMessage);
            }

            if (cleanupFailureType != null)
            {
                writer.WriteString("cleanupFailureType", cleanupFailureType);
            }
        }

        if (pendingEvents.Count > 0)
        {
            writer.WritePropertyName("pendingEvents");
            writer.WriteStartArray();
            foreach (var pendingEvent in pendingEvents)
            {
                pendingEvent.WriteTo(writer);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void PartitionEvents(
        List<JsonElement> target,
        List<JsonElement> overflowEvents,
        JsonElement payload,
        int maxCount,
        HashSet<string>? eventTypes,
        string? elementId,
        DateTimeOffset? sinceTimestamp)
    {
        if (!payload.TryGetProperty("pendingEvents", out var pendingEvents)
            || pendingEvents.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var pendingEvent in pendingEvents.EnumerateArray())
        {
            var clonedEvent = pendingEvent.Clone();
            if (target.Count < maxCount && MatchesFilters(pendingEvent, eventTypes, elementId, sinceTimestamp))
            {
                target.Add(clonedEvent);
                continue;
            }

            overflowEvents.Add(clonedEvent);
        }
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

    private static bool HasCleanupIncompleteDiagnostics(JsonElement payload) =>
        payload.TryGetProperty("cleanupIncomplete", out var cleanupIncomplete)
        && cleanupIncomplete.ValueKind == JsonValueKind.True;

    private static string? GetCleanupFailureDetail(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static JsonElement CreateReplayPreservedFailurePayload(JsonElement livePayload, JsonElement replayPayload)
    {
        if (livePayload.ValueKind != JsonValueKind.Object)
        {
            return livePayload;
        }

        var hasErrorData = livePayload.TryGetProperty("errorData", out var existingErrorData);
        var hasRecovery = livePayload.TryGetProperty("recovery", out var existingRecovery);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in livePayload.EnumerateObject())
        {
            if (property.NameEquals("errorData") && property.Value.ValueKind == JsonValueKind.Object)
            {
                WriteReplayPreservedErrorData(writer, property.Value, replayPayload);
                continue;
            }

            if (property.NameEquals("recovery") && property.Value.ValueKind == JsonValueKind.Object)
            {
                WriteReplayPreservedRecovery(writer, property.Value);
                continue;
            }

            property.WriteTo(writer);
        }

        if (!hasErrorData)
        {
            WriteReplayPreservedErrorData(writer, default, replayPayload);
        }

        if (!hasRecovery)
        {
            WriteReplayPreservedRecovery(writer, default);
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteReplayPreservedErrorData(Utf8JsonWriter writer, JsonElement existingErrorData, JsonElement replayPayload)
    {
        writer.WritePropertyName("errorData");
        writer.WriteStartObject();

        var wroteReplayPreserved = false;
        var wroteBufferedReplayEventCount = false;
        if (existingErrorData.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in existingErrorData.EnumerateObject())
            {
                if (property.NameEquals("replayPreserved"))
                {
                    wroteReplayPreserved = true;
                }
                else if (property.NameEquals("bufferedReplayEventCount"))
                {
                    wroteBufferedReplayEventCount = true;
                }

                property.WriteTo(writer);
            }
        }

        if (!wroteReplayPreserved)
        {
            writer.WriteBoolean("replayPreserved", true);
        }

        if (!wroteBufferedReplayEventCount)
        {
            writer.WriteNumber("bufferedReplayEventCount", GetIntProperty(replayPayload, "pendingEventCount"));
        }

        writer.WriteEndObject();
    }

    private static void WriteReplayPreservedRecovery(Utf8JsonWriter writer, JsonElement existingRecovery)
    {
        writer.WritePropertyName("recovery");
        writer.WriteStartObject();

        var wroteHint = false;
        var wroteSuggestedAction = false;
        if (existingRecovery.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in existingRecovery.EnumerateObject())
            {
                if (property.NameEquals("hint"))
                {
                    wroteHint = true;
                }
                else if (property.NameEquals("suggestedAction"))
                {
                    wroteSuggestedAction = true;
                }

                property.WriteTo(writer);
            }
        }

        if (!wroteHint)
        {
            writer.WriteString("hint", "Previously buffered replay events were preserved because the live drain failed before merge completed.");
        }

        if (!wroteSuggestedAction)
        {
            writer.WriteString("suggestedAction", "Retry drain_events after resolving the transient live-drain failure.");
        }

        writer.WriteEndObject();
    }

    private readonly record struct ReplayMergeResult(JsonElement ResponsePayload, JsonElement RemainingReplayPayload);
}
