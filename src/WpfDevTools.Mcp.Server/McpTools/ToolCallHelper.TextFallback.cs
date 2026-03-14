using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private const int TextFallbackMaxLength = 200;
    private const string StructuredContentFallbackMessage = "Full response available in structuredContent.";

    private static TextContentBlock CreateTextContentBlock(JsonElement payload, bool isError) => new()
    {
        Text = BuildTextFallback(payload),
        Annotations = isError ? ErrorAnnotations : null
    };

    private static string BuildTextFallback(JsonElement payload) => payload.ValueKind switch
    {
        JsonValueKind.Object => BuildObjectTextFallback(payload),
        JsonValueKind.Array => BuildArrayTextFallback(payload),
        _ => payload.GetRawText()
    };

    private static string BuildObjectTextFallback(JsonElement payload)
    {
        var fallback = new Dictionary<string, object?>();

        if (TryGetBoolProperty(payload, "success", out var success))
        {
            fallback["success"] = success;
        }

        if (TryGetStringProperty(payload, "error", out var error))
        {
            fallback["error"] = NormalizeFallbackString(error);
            if (TryGetStringProperty(payload, "errorCode", out var errorCode))
            {
                fallback["errorCode"] = errorCode;
            }
        }
        else if (TryGetStringProperty(payload, "message", out var message))
        {
            fallback["message"] = NormalizeFallbackString(message);
        }
        else
        {
            fallback["message"] = StructuredContentFallbackMessage;
        }

        fallback["hasStructuredContent"] = true;
        return JsonSerializer.Serialize(fallback, SerializerOptions);
    }

    private static string BuildArrayTextFallback(JsonElement payload) =>
        JsonSerializer.Serialize(new
        {
            message = StructuredContentFallbackMessage,
            itemCount = payload.GetArrayLength(),
            hasStructuredContent = true
        }, SerializerOptions);

    private static bool TryGetBoolProperty(JsonElement payload, string propertyName, out bool value)
    {
        if (payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetStringProperty(JsonElement payload, string propertyName, out string? value)
    {
        if (payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static string NormalizeFallbackString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return StructuredContentFallbackMessage;
        }

        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();

        return normalized.Length <= TextFallbackMaxLength
            ? normalized
            : normalized[..(TextFallbackMaxLength - 3)] + "...";
    }
}
