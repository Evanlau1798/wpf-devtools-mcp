using System.Buffers;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private static JsonElement EnsureNavigation(JsonElement element, ToolNavigationEnvelope navigation)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("nextSteps") || property.NameEquals("navigation"))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WritePropertyName("nextSteps");
        JsonSerializer.Serialize(writer, navigation.Recommended, SerializerOptions);
        writer.WritePropertyName("navigation");
        JsonSerializer.Serialize(writer, navigation, SerializerOptions);
        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static JsonElement ApplyToolSpecificContracts(
        string toolName,
        JsonElement? args,
        JsonElement element)
    {
        if (string.Equals(toolName, "get_ui_summary", StringComparison.Ordinal)
            && TryGetBool(args, "summaryOnly"))
        {
            return RemoveTopLevelProperties(element, "nodes", "navigationNodes");
        }

        if (string.Equals(toolName, "get_binding_errors", StringComparison.Ordinal)
            && TryGetBool(args, "compact"))
        {
            return RemovePropertyFromArrayItems(element, "errors", "message");
        }

        return element;
    }

    private static JsonElement NormalizePendingEventsContract(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("pendingEvents", out var pendingEvents)
            || pendingEvents.ValueKind != JsonValueKind.Array
            || pendingEvents.GetArrayLength() > 0)
        {
            return element;
        }

        return RemoveTopLevelProperties(element, "pendingEvents");
    }

    private static JsonElement NormalizeErrorContract(JsonElement element)
    {
        if (!IsToolResultError(element)
            || !TryBuildCanonicalRecovery(element, out var recovery))
        {
            return element;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        var topLevelProperties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("recovery")
                || RecoveryCompatibilityFields.Contains(property.Name, StringComparer.Ordinal))
            {
                continue;
            }

            topLevelProperties.Add(property.Name);
            property.WriteTo(writer);
        }

        foreach (var propertyName in RecoveryCompatibilityFields)
        {
            if (topLevelProperties.Contains(propertyName)
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
        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static bool TryBuildCanonicalRecovery(JsonElement element, out JsonElement recovery)
    {
        var recoveryProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (element.TryGetProperty("recovery", out var existingRecovery)
            && existingRecovery.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in existingRecovery.EnumerateObject())
            {
                recoveryProperties[property.Name] = property.Value.Clone();
            }
        }

        foreach (var propertyName in RecoveryCompatibilityFields)
        {
            if (recoveryProperties.ContainsKey(propertyName)
                || !element.TryGetProperty(propertyName, out var property)
                || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            recoveryProperties[propertyName] = property.Clone();
        }

        if (recoveryProperties.Count == 0)
        {
            recovery = default;
            return false;
        }

        recovery = JsonSerializer.SerializeToElement(recoveryProperties, SerializerOptions);
        return true;
    }

    private static JsonElement RemoveTopLevelProperties(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object || propertyNames.Length == 0)
        {
            return element;
        }

        var propertiesToRemove = new HashSet<string>(propertyNames, StringComparer.Ordinal);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in element.EnumerateObject())
        {
            if (propertiesToRemove.Contains(property.Name))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static JsonElement RemovePropertyFromArrayItems(
        JsonElement element,
        string arrayPropertyName,
        string propertyToRemove)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(arrayPropertyName, out var arrayProperty)
            || arrayProperty.ValueKind != JsonValueKind.Array)
        {
            return element;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(arrayPropertyName))
            {
                property.WriteTo(writer);
                continue;
            }

            writer.WritePropertyName(property.Name);
            writer.WriteStartArray();
            foreach (var item in arrayProperty.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    item.WriteTo(writer);
                    continue;
                }

                writer.WriteStartObject();
                foreach (var itemProperty in item.EnumerateObject())
                {
                    if (itemProperty.NameEquals(propertyToRemove))
                    {
                        continue;
                    }

                    itemProperty.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static int? TryGetProcessId(JsonElement? args)
    {
        if (args is not { } candidate
            || candidate.ValueKind != JsonValueKind.Object
            || !candidate.TryGetProperty("processId", out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var processId))
        {
            return null;
        }

        return processId;
    }

    private static (bool RequiresReconnect, int? ProcessId) ResolveTimeoutRecovery(
        string toolName,
        JsonElement? args,
        SessionManager? sessionManager = null)
    {
        if (TimeoutReconnectOptOutTools.Contains(toolName))
        {
            return (false, null);
        }

        var processId = TryGetProcessId(args);
        if (processId is null
            && sessionManager is not null
            && sessionManager.TryGetActiveProcessId(out var activeProcessId))
        {
            processId = activeProcessId;
        }

        if (processId is null)
        {
            return (false, null);
        }

        return (true, processId);
    }

    private static bool ShouldIncludeNavigation(string toolName, JsonElement? args)
    {
        if (args is not { } candidate
            || candidate.ValueKind != JsonValueKind.Object
            || !candidate.TryGetProperty("navigation", out var property)
            || property.ValueKind != JsonValueKind.False)
        {
            return true;
        }

        return !NavigationOptOutTools.Contains(toolName);
    }

    private static bool TryGetBool(JsonElement? args, string propertyName)
    {
        return args is { } candidate
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();
    }
}
