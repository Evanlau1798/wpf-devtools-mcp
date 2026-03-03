using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfDevTools.Shared.Messages;

/// <summary>
/// Represents a request sent from MCP Server to Inspector DLL
/// </summary>
public sealed class InspectorRequest
{
    /// <summary>
    /// Unique request identifier for correlation
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Method name to invoke (e.g., "get_visual_tree")
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Method parameters as JSON element
    /// </summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; init; }
}
