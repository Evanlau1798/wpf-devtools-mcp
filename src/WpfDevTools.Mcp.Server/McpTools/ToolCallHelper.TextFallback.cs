using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private const int TextFallbackMaxLength = 200;
    private const int TextFallbackInlineStringMaxLength = 80;
    private const int TextFallbackMaxSummaryFields = 6;
    private const string StructuredContentFallbackMessage = "Full response available in structuredContent.";

    private static readonly HashSet<string> OmittedTextFallbackProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "error",
        "message",
        "content",
        "structuredContent",
        "navigation",
        "nextSteps",
        "base64Image",
        "xaml",
        "markup",
        "html",
        "rawText",
        "rawXaml"
    };

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
        var hasPrimaryMessage = false;
        var baseFieldCount = 0;

        if (TryGetBoolProperty(payload, "success", out var success))
        {
            fallback["success"] = success;
            baseFieldCount++;
        }

        if (TryGetStringProperty(payload, "error", out var error))
        {
            fallback["error"] = NormalizeFallbackString(error);
            hasPrimaryMessage = true;
            if (TryGetStringProperty(payload, "errorCode", out var errorCode))
            {
                fallback["errorCode"] = errorCode;
            }
        }
        else if (TryGetStringProperty(payload, "message", out var message))
        {
            fallback["message"] = NormalizeFallbackString(message);
            hasPrimaryMessage = true;
        }

        AppendHighSignalFallbackFields(payload, fallback);

        if (!hasPrimaryMessage && fallback.Count == baseFieldCount)
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

    private static void AppendHighSignalFallbackFields(JsonElement payload, Dictionary<string, object?> fallback)
    {
        var remainingFields = TextFallbackMaxSummaryFields;

        foreach (var property in payload.EnumerateObject())
        {
            if (remainingFields <= 0
                || fallback.ContainsKey(property.Name)
                || OmittedTextFallbackProperties.Contains(property.Name))
            {
                continue;
            }

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.True:
                case JsonValueKind.False:
                    fallback[property.Name] = property.Value.GetBoolean();
                    remainingFields--;
                    break;

                case JsonValueKind.Number:
                    fallback[property.Name] = ReadFallbackNumber(property.Value);
                    remainingFields--;
                    break;

                case JsonValueKind.String:
                    var stringValue = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        continue;
                    }

                    fallback[property.Name] = NormalizeFallbackString(stringValue, TextFallbackInlineStringMaxLength);
                    remainingFields--;
                    break;
            }
        }

        if (remainingFields <= 0)
        {
            return;
        }

        foreach (var property in payload.EnumerateObject())
        {
            if (remainingFields <= 0
                || fallback.ContainsKey(property.Name)
                || OmittedTextFallbackProperties.Contains(property.Name)
                || property.Value.ValueKind != JsonValueKind.Array
                || HasRelatedCountScalar(payload, property.Name))
            {
                continue;
            }

            fallback[$"{property.Name}Count"] = property.Value.GetArrayLength();
            remainingFields--;
        }
    }

    private static bool HasRelatedCountScalar(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty($"{propertyName}Count", out var directCount)
                && directCount.ValueKind == JsonValueKind.Number
            || TryGetConventionalSingularCountProperty(payload, propertyName, out _);
    }

    private static bool TryGetConventionalSingularCountProperty(JsonElement payload, string propertyName, out JsonElement property)
    {
        if (propertyName.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
        {
            return payload.TryGetProperty($"{propertyName[..^3]}yCount", out property)
                && property.ValueKind == JsonValueKind.Number;
        }

        if (propertyName.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return payload.TryGetProperty($"{propertyName[..^1]}Count", out property)
                && property.ValueKind == JsonValueKind.Number;
        }

        property = default;
        return false;
    }

    private static object ReadFallbackNumber(JsonElement propertyValue)
    {
        if (propertyValue.TryGetInt64(out var intValue))
        {
            return intValue;
        }

        if (propertyValue.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        return propertyValue.GetDouble();
    }

    private static string NormalizeFallbackString(string? value, int maxLength = TextFallbackMaxLength)
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

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 3)] + "...";
    }
}
