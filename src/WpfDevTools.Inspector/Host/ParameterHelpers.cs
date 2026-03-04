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
        {
            if (property.ValueKind == JsonValueKind.String)
                return property.GetString();
            return null;
        }

        return null;
    }

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

    public static T? GetObjectParam<T>(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return default;

        if (@params.Value.TryGetProperty(name, out var property))
            return JsonSerializer.Deserialize<T>(property.GetRawText());

        return default;
    }
}
