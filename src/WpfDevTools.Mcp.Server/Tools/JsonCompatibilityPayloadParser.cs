using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class JsonCompatibilityPayloadParser
{
    internal static bool TryParseOptionalObjectProperty(
        JsonElement root,
        string propertyName,
        out JsonElement parsedValue,
        out bool hasValue,
        out string? errorMessage)
    {
        parsedValue = default;
        hasValue = false;
        errorMessage = null;

        if (!root.TryGetProperty(propertyName, out var rawValue))
        {
            return true;
        }

        hasValue = true;
        if (rawValue.ValueKind == JsonValueKind.Object)
        {
            parsedValue = rawValue.Clone();
            return true;
        }

        if (rawValue.ValueKind != JsonValueKind.String)
        {
            errorMessage = $"{propertyName} must be an object or a stringified JSON object when provided.";
            return false;
        }

        var serializedObject = rawValue.GetString();
        if (string.IsNullOrWhiteSpace(serializedObject))
        {
            errorMessage = $"{propertyName} string payload must not be empty.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(serializedObject);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = $"Stringified {propertyName} payload must decode to a JSON object.";
                return false;
            }

            parsedValue = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            errorMessage = $"{propertyName} string payload must contain valid JSON.";
            return false;
        }
    }
}
