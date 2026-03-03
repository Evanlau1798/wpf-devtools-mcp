using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfDevTools.Shared.Messages;

/// <summary>
/// Represents an event pushed from Inspector to MCP Server
/// </summary>
public sealed class EventMessage
{
    /// <summary>
    /// Event type (e.g., "binding_error", "property_changed")
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Event data as JSON element
    /// </summary>
    [JsonPropertyName("data")]
    public required JsonElement Data { get; init; }
}
