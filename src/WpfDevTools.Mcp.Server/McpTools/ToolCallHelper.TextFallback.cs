using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private const int TextFallbackMaxLength = 200;
    private const int TextFallbackInlineStringMaxLength = 80;
    private const string FullTextFallbackMode = "full";
    private const string StructuredContentFallbackMessage = "Canonical payload available in structuredContent; content[0].text is a compact fallback.";

    private static readonly HashSet<string> OmittedTextFallbackProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "error",
        "data",
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
        Text = BuildTextFallback(payload, ResolveTextFallbackMode()),
        Annotations = isError ? ErrorAnnotations : null
    };

    private static string? ResolveTextFallbackMode() =>
        Environment.GetEnvironmentVariable(McpServerConfiguration.TextFallbackModeEnvVar);

    private static string BuildTextFallback(JsonElement payload, string? fallbackMode)
    {
        if (string.Equals(fallbackMode, FullTextFallbackMode, StringComparison.OrdinalIgnoreCase))
        {
            return payload.GetRawText();
        }

        return BuildCompactTextFallback(payload);
    }

    private static string BuildCompactTextFallback(JsonElement payload) => payload.ValueKind switch
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

        AppendRecoveryFallbackFields(payload, fallback);
        AppendHighSignalFallbackFields(payload, fallback);

        if (!hasPrimaryMessage && fallback.Count == baseFieldCount)
        {
            fallback["message"] = StructuredContentFallbackMessage;
        }

        fallback["hasStructuredContent"] = true;
        return JsonSerializer.Serialize(fallback, SerializerOptions);
    }

    private static void AppendRecoveryFallbackFields(JsonElement payload, Dictionary<string, object?> fallback)
    {
        if (!payload.TryGetProperty("recovery", out var recovery)
            || recovery.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AppendRecoveryScalar(recovery, fallback, "suggestedAction");
        AppendRecoveryScalar(recovery, fallback, "hint");
        AppendRecoveryScalar(recovery, fallback, "requiresReconnect");
        AppendRecoveryScalar(recovery, fallback, "processId");
        AppendRecoveryScalar(recovery, fallback, "timeoutSeconds");
        AppendRecoveryScalar(recovery, fallback, "retryAfterSeconds");
        AppendRecoveryScalar(recovery, fallback, "retryAfter");
        AppendRecoveryScalar(recovery, fallback, "availableTokens");
        AppendRecoveryStringArray(recovery, fallback, "availableEvents");
    }

    private static void AppendRecoveryScalar(JsonElement recovery, Dictionary<string, object?> fallback, string propertyName)
    {
        if (fallback.ContainsKey(propertyName)
            || !recovery.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                fallback[propertyName] = property.GetBoolean();
                break;

            case JsonValueKind.Number:
                fallback[propertyName] = ReadFallbackNumber(property);
                break;

            case JsonValueKind.String:
                var stringValue = property.GetString();
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    fallback[propertyName] = NormalizeFallbackString(stringValue, TextFallbackInlineStringMaxLength);
                }
                break;
        }
    }

    private static void AppendRecoveryStringArray(JsonElement recovery, Dictionary<string, object?> fallback, string propertyName)
    {
        if (fallback.ContainsKey(propertyName)
            || !recovery.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var values = property.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Take(5)
            .ToArray();

        if (values.Length > 0)
        {
            fallback[propertyName] = values;
        }
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
        foreach (var property in payload.EnumerateObject())
        {
            if (fallback.ContainsKey(property.Name)
                || OmittedTextFallbackProperties.Contains(property.Name))
            {
                continue;
            }

            AppendScalarFallbackField(property, fallback);
        }

        foreach (var property in payload.EnumerateObject())
        {
            if (fallback.ContainsKey(property.Name)
                || OmittedTextFallbackProperties.Contains(property.Name)
                || property.Value.ValueKind != JsonValueKind.Array
                || HasRelatedCountScalar(payload, property.Name))
            {
                continue;
            }

            fallback[$"{property.Name}Count"] = property.Value.GetArrayLength();
        }
    }

    private static void AppendScalarFallbackField(
        JsonProperty property,
        Dictionary<string, object?> fallback)
    {
        switch (property.Value.ValueKind)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                fallback[property.Name] = property.Value.GetBoolean();
                break;

            case JsonValueKind.Number:
                fallback[property.Name] = ReadFallbackNumber(property.Value);
                break;

            case JsonValueKind.String:
                var stringValue = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    fallback[property.Name] = NormalizeFallbackString(stringValue, TextFallbackInlineStringMaxLength);
                }
                break;
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
