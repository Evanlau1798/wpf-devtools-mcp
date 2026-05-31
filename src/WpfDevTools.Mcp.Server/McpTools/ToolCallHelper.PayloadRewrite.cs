using System.Buffers;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private static JsonElement NormalizeToolPayload(
        string toolName,
        JsonElement? args,
        JsonElement element,
        ToolNavigationEnvelope? navigation)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        var removeUiSummaryNodes = string.Equals(toolName, "get_ui_summary", StringComparison.Ordinal)
            && TryGetBool(args, "summaryOnly");
        var removeBindingErrorMessages = string.Equals(toolName, "get_binding_errors", StringComparison.Ordinal)
            && TryGetBool(args, "compact");
        var removeEmptyPendingEvents = HasEmptyPendingEvents(element);
        var recovery = default(JsonElement);
        var hasRecovery = IsToolResultError(element)
            && TryBuildCanonicalRecovery(element, out recovery);

        if (navigation is null
            && !removeUiSummaryNodes
            && !removeBindingErrorMessages
            && !removeEmptyPendingEvents
            && !hasRecovery)
        {
            return element;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        var writtenProperties = hasRecovery
            ? new HashSet<string>(StringComparer.Ordinal)
            : null;

        writer.WriteStartObject();
        foreach (var property in element.EnumerateObject())
        {
            if (ShouldRemoveTopLevelProperty(
                property,
                navigation is not null,
                removeUiSummaryNodes,
                removeEmptyPendingEvents,
                hasRecovery))
            {
                continue;
            }

            if (removeBindingErrorMessages && property.NameEquals("errors"))
            {
                writer.WritePropertyName(property.Name);
                WriteArrayWithoutItemProperty(writer, property.Value, "message");
            }
            else
            {
                property.WriteTo(writer);
            }

            writtenProperties?.Add(property.Name);
        }

        if (navigation is not null)
        {
            writer.WritePropertyName("nextSteps");
            JsonSerializer.Serialize(writer, navigation.Recommended, SerializerOptions);
            writer.WritePropertyName("navigation");
            JsonSerializer.Serialize(writer, navigation, SerializerOptions);
        }

        if (hasRecovery)
        {
            WriteRecoveryProjection(writer, recovery, writtenProperties!);
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static bool HasEmptyPendingEvents(JsonElement element) =>
        element.TryGetProperty("pendingEvents", out var pendingEvents)
        && pendingEvents.ValueKind == JsonValueKind.Array
        && pendingEvents.GetArrayLength() == 0;

    private static bool ShouldRemoveTopLevelProperty(
        JsonProperty property,
        bool replacingNavigation,
        bool removeUiSummaryNodes,
        bool removeEmptyPendingEvents,
        bool hasRecovery)
    {
        if (replacingNavigation
            && (property.NameEquals("nextSteps") || property.NameEquals("navigation")))
        {
            return true;
        }

        if (removeUiSummaryNodes
            && (property.NameEquals("nodes") || property.NameEquals("navigationNodes")))
        {
            return true;
        }

        if (removeEmptyPendingEvents && property.NameEquals("pendingEvents"))
        {
            return true;
        }

        return hasRecovery
            && (property.NameEquals("recovery")
                || RecoveryCompatibilityFields.Contains(property.Name, StringComparer.Ordinal));
    }

    private static void WriteArrayWithoutItemProperty(
        Utf8JsonWriter writer,
        JsonElement array,
        string propertyToRemove)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            array.WriteTo(writer);
            return;
        }

        writer.WriteStartArray();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                item.WriteTo(writer);
                continue;
            }

            writer.WriteStartObject();
            foreach (var itemProperty in item.EnumerateObject())
            {
                if (!itemProperty.NameEquals(propertyToRemove))
                {
                    itemProperty.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRecoveryProjection(
        Utf8JsonWriter writer,
        JsonElement recovery,
        HashSet<string> writtenProperties)
    {
        foreach (var propertyName in RecoveryCompatibilityFields)
        {
            if (writtenProperties.Contains(propertyName)
                || !recovery.TryGetProperty(propertyName, out var projectedProperty)
                || projectedProperty.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            writer.WritePropertyName(propertyName);
            projectedProperty.WriteTo(writer);
        }

        writer.WritePropertyName("recovery");
        recovery.WriteTo(writer);
    }
}
