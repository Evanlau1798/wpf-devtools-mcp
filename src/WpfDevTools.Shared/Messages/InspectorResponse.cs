using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfDevTools.Shared.Messages;

/// <summary>
/// Represents a response from Inspector DLL to MCP Server
/// </summary>
public sealed class InspectorResponse
{
    /// <summary>
    /// Request identifier for correlation
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Result data (null if error occurred)
    /// </summary>
    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    /// <summary>
    /// Error information (null if successful)
    /// </summary>
    [JsonPropertyName("error")]
    public InspectorError? Error { get; init; }

    /// <summary>
    /// Correlation ID for tracing requests across the entire call chain
    /// Optional - used for observability and debugging
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }
}
