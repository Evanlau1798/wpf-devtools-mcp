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
            return prop.GetString();
        return null;
    }

    /// <summary>
    /// Parse an integer parameter from JSON arguments
    /// </summary>
    public static int? ParseIntParam(JsonElement? arguments, string paramName)
    {
        if (arguments.HasValue && arguments.Value.TryGetProperty(paramName, out var prop))
            return prop.GetInt32();
        return null;
    }

    /// <summary>
    /// Parse a boolean parameter from JSON arguments
    /// </summary>
    public static bool? ParseBoolParam(JsonElement? arguments, string paramName)
    {
        if (arguments.HasValue && arguments.Value.TryGetProperty(paramName, out var prop))
            return prop.GetBoolean();
        return null;
    }
}
