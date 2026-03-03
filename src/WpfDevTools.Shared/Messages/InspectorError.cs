using System.Text.Json;
using System.Text.Json.Serialization;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Shared.Messages;

/// <summary>
/// Represents an error in Inspector operation
/// </summary>
public sealed class InspectorError
{
    /// <summary>
    /// Error code
    /// </summary>
    [JsonPropertyName("code")]
    public required ErrorCode Code { get; init; }

    /// <summary>
    /// Human-readable error message
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Additional error data
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}
