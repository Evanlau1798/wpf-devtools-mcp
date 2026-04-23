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
}
