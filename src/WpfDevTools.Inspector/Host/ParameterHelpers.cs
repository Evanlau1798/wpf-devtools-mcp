using System.Text.Json;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Helper methods for parsing request parameters
/// </summary>
public static class ParameterHelpers
{
    public static string? GetStringParam(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return null;

        if (@params.Value.TryGetProperty(name, out var property))
            return property.GetString();

        return null;
    }

    public static int? GetIntParam(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return null;

        if (@params.Value.TryGetProperty(name, out var property))
            return property.GetInt32();

        return null;
    }

    public static T? GetObjectParam<T>(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return default;

        if (@params.Value.TryGetProperty(name, out var property))
            return JsonSerializer.Deserialize<T>(property.GetRawText());

        return default;
    }
}
