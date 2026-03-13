using System.Text.Json;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Helper methods for parsing request parameters
/// </summary>
public static class ParameterHelpers
{
    private const int MaxJsonSizeBytes = 1 * 1024 * 1024; // 1 MB limit for security

    // SECURITY: Configure JsonSerializerOptions with depth and size limits
    private static readonly JsonSerializerOptions SecureJsonOptions = new JsonSerializerOptions
    {
        MaxDepth = 32, // Prevent deeply nested JSON attacks
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Get string parameter from JSON arguments
    /// </summary>
    /// <param name="params">JSON parameters</param>
    /// <param name="name">Parameter name</param>
    /// <returns>String value if found, null otherwise</returns>
    public static string? GetStringParam(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return null;

        if (@params.Value.TryGetProperty(name, out var property))
        {
            if (property.ValueKind == JsonValueKind.String)
                return property.GetString();
            return null;
        }

        return null;
    }

    /// <summary>
    /// Get string array parameter from JSON arguments.
    /// </summary>
    /// <param name="params">JSON parameters</param>
    /// <param name="name">Parameter name</param>
    /// <returns>String array if found, null otherwise</returns>
    public static string[]? GetStringArrayParam(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return null;

        if (!@params.Value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
            return null;

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                item.GetString() is string value &&
                !string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values.ToArray();
    }

    /// <summary>
    /// Get integer parameter from JSON arguments
    /// </summary>
    /// <param name="params">JSON parameters</param>
    /// <param name="name">Parameter name</param>
    /// <returns>Integer value if found, null otherwise</returns>
    public static int? GetIntParam(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue) return null;
        if (@params.Value.TryGetProperty(name, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var val))
                return val;
        }
        return null;
    }

    /// <summary>
    /// Get boolean parameter from JSON arguments
    /// </summary>
    /// <param name="params">JSON parameters</param>
    /// <param name="name">Parameter name</param>
    /// <returns>Boolean value if found, null otherwise</returns>
    public static bool? GetBoolParam(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue) return null;
        if (@params.Value.TryGetProperty(name, out var property))
        {
            if (property.ValueKind == JsonValueKind.True) return true;
            if (property.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    /// <summary>
    /// Get object parameter from JSON arguments and deserialize to type T
    /// </summary>
    /// <typeparam name="T">Type to deserialize to</typeparam>
    /// <param name="params">JSON parameters</param>
    /// <param name="name">Parameter name</param>
    /// <returns>Deserialized object if found, default(T) otherwise</returns>
    public static T? GetObjectParam<T>(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return default;

        if (@params.Value.TryGetProperty(name, out var property))
        {
            var rawText = property.GetRawText();

            // SECURITY: Check size limit before deserialization to prevent memory exhaustion
            if (rawText.Length > MaxJsonSizeBytes)
            {
                throw new InvalidOperationException(
                    $"JSON payload too large: {rawText.Length} bytes (max: {MaxJsonSizeBytes} bytes)");
            }

            // SECURITY: Use SecureJsonOptions with MaxDepth = 32 to prevent stack overflow
            return JsonSerializer.Deserialize<T>(rawText, SecureJsonOptions);
        }

        return default;
    }
}
