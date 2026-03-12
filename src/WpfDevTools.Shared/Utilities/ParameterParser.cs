using System.Text.Json;
using System.Text.RegularExpressions;

namespace WpfDevTools.Shared.Utilities;

/// <summary>
/// Utility class for parsing parameters from JSON arguments
/// </summary>
public static class ParameterParser
{
    private const int MaxElementIdLength = 256;

    // SECURITY: Only allow alphanumeric, hyphen, and underscore to prevent path traversal and injection attacks
    private static readonly Regex ElementIdPattern = new Regex(
        @"^[a-zA-Z0-9_-]+$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Parse a string parameter from JSON arguments
    /// </summary>
    /// <param name="arguments">JSON element containing arguments</param>
    /// <param name="paramName">Name of parameter to parse</param>
    /// <returns>String value if parameter exists and is a string, null otherwise</returns>
    public static string? ParseStringParam(JsonElement? arguments, string paramName)
    {
        if (arguments.HasValue && arguments.Value.TryGetProperty(paramName, out var prop))
        {
            // Only return string if the value is actually a string type
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    /// <summary>
    /// Parse a string array parameter from JSON arguments.
    /// </summary>
    /// <param name="arguments">JSON element containing arguments</param>
    /// <param name="paramName">Name of parameter to parse</param>
    /// <returns>String array value if parameter exists and is an array of strings, null otherwise</returns>
    public static string[]? ParseStringArrayParam(JsonElement? arguments, string paramName)
    {
        if (!arguments.HasValue || !arguments.Value.TryGetProperty(paramName, out var prop))
        {
            return null;
        }

        if (prop.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }
        }

        return values.ToArray();
    }

    /// <summary>
    /// Parse an integer parameter from JSON arguments
    /// </summary>
    /// <param name="arguments">JSON element containing arguments</param>
    /// <param name="paramName">Name of parameter to parse</param>
    /// <returns>Integer value if parameter exists and can be parsed as int, null otherwise</returns>
    public static int? ParseIntParam(JsonElement? arguments, string paramName)
    {
        if (arguments.HasValue && arguments.Value.TryGetProperty(paramName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return prop.GetInt32();
                }
                catch (FormatException)
                {
                    // Value is a number but not an int (e.g., double or overflow)
                    return null;
                }
            }

            // Handle string values by trying to parse
            if (prop.ValueKind == JsonValueKind.String)
            {
                var strValue = prop.GetString();
                if (int.TryParse(strValue, out var intValue))
                    return intValue;
            }
        }
        return null;
    }

    /// <summary>
    /// Parse a boolean parameter from JSON arguments
    /// </summary>
    /// <param name="arguments">JSON element containing arguments</param>
    /// <param name="paramName">Name of parameter to parse</param>
    /// <returns>Boolean value if parameter exists and can be parsed as bool, null otherwise</returns>
    public static bool? ParseBoolParam(JsonElement? arguments, string paramName)
    {
        if (arguments.HasValue && arguments.Value.TryGetProperty(paramName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                return prop.GetBoolean();

            // Handle string values "true"/"false"
            if (prop.ValueKind == JsonValueKind.String)
            {
                var strValue = prop.GetString();
                if (bool.TryParse(strValue, out var boolValue))
                    return boolValue;
            }
        }
        return null;
    }

    /// <summary>
    /// Parse a raw JSON parameter from JSON arguments
    /// </summary>
    /// <param name="arguments">JSON element containing arguments</param>
    /// <param name="paramName">Name of parameter to parse</param>
    /// <returns>Cloned JsonElement if parameter exists, null otherwise</returns>
    public static JsonElement? ParseJsonParam(JsonElement? arguments, string paramName)
    {
        if (arguments.HasValue && arguments.Value.TryGetProperty(paramName, out var prop))
        {
            return prop.Clone();
        }

        return null;
    }

    /// <summary>
    /// Validate elementId format to prevent path traversal and injection attacks
    /// </summary>
    /// <param name="elementId">Element ID to validate</param>
    /// <param name="error">Error message if validation fails</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool ValidateElementId(string? elementId, out string? error)
    {
        error = null;

        if (string.IsNullOrEmpty(elementId))
        {
            error = "elementId cannot be null or empty";
            return false;
        }

        // SECURITY: Check length to prevent DoS
        if (elementId!.Length > MaxElementIdLength)
        {
            error = $"elementId too long (max {MaxElementIdLength} characters)";
            return false;
        }

        // SECURITY: Check for null bytes
        if (elementId.Contains('\0'))
        {
            error = "elementId contains invalid null byte";
            return false;
        }

        // SECURITY: Only allow alphanumeric, hyphen, and underscore
        try
        {
            if (!ElementIdPattern.IsMatch(elementId))
            {
                error = "elementId contains invalid characters (only alphanumeric, hyphen, and underscore allowed)";
                return false;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            error = "elementId validation timed out";
            return false;
        }

        return true;
    }
}
