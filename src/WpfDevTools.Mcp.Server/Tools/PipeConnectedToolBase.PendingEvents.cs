using System.Buffers;
using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public abstract partial class PipeConnectedToolBase
{
    private static bool HasCleanupIncompleteDiagnostics(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("cleanupIncomplete", out var cleanupIncomplete)
        && cleanupIncomplete.ValueKind == JsonValueKind.True;

    private static void WriteCleanupDiagnostics(Utf8JsonWriter writer, JsonElement payload)
    {
        if (!HasCleanupIncompleteDiagnostics(payload))
        {
            return;
        }

        writer.WriteBoolean("cleanupIncomplete", true);

        if (payload.TryGetProperty("cleanupFailureMessage", out var cleanupFailureMessage))
        {
            writer.WritePropertyName("cleanupFailureMessage");
            cleanupFailureMessage.WriteTo(writer);
        }

        if (payload.TryGetProperty("cleanupFailureType", out var cleanupFailureType))
        {
            writer.WritePropertyName("cleanupFailureType");
            cleanupFailureType.WriteTo(writer);
        }
    }

    private static void WritePiggybackPendingEvents(Utf8JsonWriter writer, JsonElement pendingEvents)
    {
        if (pendingEvents.ValueKind != JsonValueKind.Array)
        {
            pendingEvents.WriteTo(writer);
            return;
        }

        writer.WriteStartArray();
        foreach (var pendingEvent in pendingEvents.EnumerateArray())
        {
            WritePiggybackPendingEvent(writer, pendingEvent);
        }

        writer.WriteEndArray();
    }

    private static void WritePiggybackPendingEvent(Utf8JsonWriter writer, JsonElement pendingEvent)
    {
        if (pendingEvent.ValueKind != JsonValueKind.Object)
        {
            pendingEvent.WriteTo(writer);
            return;
        }

        var isBindingError = pendingEvent.TryGetProperty("eventType", out var eventType)
            && eventType.ValueKind == JsonValueKind.String
            && string.Equals(eventType.GetString(), "BindingError", StringComparison.Ordinal);

        writer.WriteStartObject();
        foreach (var property in pendingEvent.EnumerateObject())
        {
            if (property.NameEquals("sourceKey"))
            {
                continue;
            }

            if (isBindingError && property.NameEquals("newValue"))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static object MergePendingEvents(JsonElement primaryPayload, JsonElement drainPayload)
    {
        if (primaryPayload.ValueKind != JsonValueKind.Object)
        {
            return primaryPayload;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in primaryPayload.EnumerateObject())
        {
            if (property.NameEquals("pendingEvents")
                || property.NameEquals("pendingEventCount")
                || property.NameEquals("droppedEventCount"))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        if (drainPayload.TryGetProperty("pendingEventCount", out var pendingEventCount))
        {
            writer.WritePropertyName("pendingEventCount");
            pendingEventCount.WriteTo(writer);
        }

        if (drainPayload.TryGetProperty("droppedEventCount", out var droppedEventCount))
        {
            writer.WritePropertyName("droppedEventCount");
            droppedEventCount.WriteTo(writer);
        }

        WriteCleanupDiagnostics(writer, drainPayload);

        writer.WriteString("pendingEventsOrigin", "piggybackSharedBuffer");
        writer.WriteBoolean("pendingEventsMayIncludePriorContext", true);
        writer.WriteString(
            "pendingEventsSuggestedAction",
            "For a clean event window, call drain_events with narrow filters before the next action, perform the action, then call drain_events again.");
        writer.WriteBoolean("pendingEventsAreAdvisory", true);
        writer.WriteString("pendingEventsSummary", BuildPendingEventsSummary(drainPayload));

        if (drainPayload.TryGetProperty("pendingEvents", out var pendingEvents))
        {
            writer.WritePropertyName("pendingEvents");
            WritePiggybackPendingEvents(writer, pendingEvents);
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static string BuildPendingEventsSummary(JsonElement drainPayload)
    {
        var pendingEventCount = drainPayload.TryGetProperty("pendingEventCount", out var countProperty)
            && countProperty.ValueKind == JsonValueKind.Number
            && countProperty.TryGetInt32(out var count)
                ? count
                : 0;
        var eventTypes = GetPendingEventTypes(drainPayload);
        var typeSummary = eventTypes.Length > 0
            ? $" Types: {string.Join(", ", eventTypes)}."
            : string.Empty;
        return $"{pendingEventCount} piggybacked shared-buffer event(s) are advisory and may include prior context from before this request.{typeSummary} Use drain_events before and after an action when a clean event window matters.";
    }

    private static string[] GetPendingEventTypes(JsonElement drainPayload)
    {
        if (!drainPayload.TryGetProperty("pendingEvents", out var pendingEvents)
            || pendingEvents.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return pendingEvents.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => item.TryGetProperty("eventType", out var eventType)
                && eventType.ValueKind == JsonValueKind.String
                    ? eventType.GetString()
                    : null)
            .Where(eventType => !string.IsNullOrWhiteSpace(eventType))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static object MergePiggybackFailureDiagnostics(
        object primaryResult,
        int processId,
        string failureType,
        string? errorCode,
        string? errorMessage)
    {
        try
        {
            var primaryPayload = ToJsonElement(primaryResult);
            if (!IsSuccessfulPayload(primaryPayload))
            {
                return primaryResult;
            }

            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            var requiresReconnect = PiggybackFailureRequiresReconnect(failureType);
            var suggestedAction = BuildPiggybackFailureSuggestedAction(processId, requiresReconnect);
            writer.WriteStartObject();

            foreach (var property in primaryPayload.EnumerateObject())
            {
                if (property.NameEquals("pendingEventsPiggybackFailed")
                    || property.NameEquals("pendingEventsPiggybackFailureType")
                    || property.NameEquals("pendingEventsMayRemainBuffered")
                    || property.NameEquals("pendingEventsPiggybackRequiresReconnect")
                    || property.NameEquals("pendingEventsStateAfterTimeoutUnknown")
                    || property.NameEquals("pendingEventsPiggybackSuggestedAction")
                    || property.NameEquals("pendingEventsPiggybackErrorCode")
                    || property.NameEquals("pendingEventsPiggybackError")
                    || (requiresReconnect && property.NameEquals("requiresReconnect"))
                    || (requiresReconnect && property.NameEquals("stateAfterTimeoutUnknown"))
                    || (requiresReconnect && property.NameEquals("suggestedAction")))
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteBoolean("pendingEventsPiggybackFailed", true);
            writer.WriteString("pendingEventsPiggybackFailureType", failureType);
            writer.WriteBoolean("pendingEventsMayRemainBuffered", true);
            writer.WriteBoolean("pendingEventsPiggybackRequiresReconnect", requiresReconnect);
            writer.WriteString("pendingEventsPiggybackSuggestedAction", suggestedAction);
            if (requiresReconnect)
            {
                writer.WriteBoolean("requiresReconnect", true);
                writer.WriteBoolean("stateAfterTimeoutUnknown", true);
                writer.WriteBoolean("pendingEventsStateAfterTimeoutUnknown", true);
                writer.WriteString("suggestedAction", suggestedAction);
            }

            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                writer.WriteString("pendingEventsPiggybackErrorCode", errorCode);
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                writer.WriteString("pendingEventsPiggybackError", errorMessage);
            }

            writer.WriteEndObject();
            writer.Flush();

            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            return document.RootElement.Clone();
        }
        catch (Exception)
        {
            return primaryResult;
        }
    }

    private static string ResolvePiggybackFailureType(JsonElement payload)
    {
        var errorCode = GetStringProperty(payload, "errorCode");
        return errorCode switch
        {
            "Timeout" => "Timeout",
            "TransportReset" => "TransportReset",
            _ => "NonSuccessResponse"
        };
    }

    private static string ResolvePiggybackFailureType(Exception ex) => ex switch
    {
        OperationCanceledException or TimeoutException => "Timeout",
        System.IO.IOException or ObjectDisposedException or InvalidOperationException => "TransportReset",
        _ => ex.GetType().Name
    };

    private static bool PiggybackFailureRequiresReconnect(string failureType) =>
        string.Equals(failureType, "Timeout", StringComparison.Ordinal)
        || string.Equals(failureType, "TransportReset", StringComparison.Ordinal);

    private static string BuildPiggybackFailureSuggestedAction(int processId, bool requiresReconnect) =>
        requiresReconnect
            ? $"Reconnect to process {processId}, re-read target state, then call drain_events explicitly before relying on event absence."
            : "Call drain_events explicitly to recover any buffered pending events before relying on event absence.";
}
