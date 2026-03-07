using System.Text.Json;

namespace WpfDevTools.Tests.Unit;

/// <summary>
/// Shared test helper methods
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Convert an anonymous object to JsonElement for tool parameter passing
    /// </summary>
    public static JsonElement ToJsonElement(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}

