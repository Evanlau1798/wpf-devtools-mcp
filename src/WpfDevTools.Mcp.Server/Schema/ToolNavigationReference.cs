using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfDevTools.Mcp.Server.Schema;

public sealed class ToolNavigationReference
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement> Properties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public static ToolNavigationReference Create(string type, params (string name, object? value)[] properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in properties)
        {
            if (string.IsNullOrWhiteSpace(name) || value is null)
            {
                continue;
            }

            values[name] = JsonSerializer.SerializeToElement(value);
        }

        return new ToolNavigationReference
        {
            Type = type,
            Properties = values
        };
    }
}
