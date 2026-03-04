using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Utility class for parsing parameters from JSON arguments
/// </summary>
public static class ParameterParser
{
    /// <summary>
    /// Parse a string parameter from JSON arguments
    /// </summary>
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
    /// Parse an integer parameter from JSON arguments
    /// </summary>
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
}
