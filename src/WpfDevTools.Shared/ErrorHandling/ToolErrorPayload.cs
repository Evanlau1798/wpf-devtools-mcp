using System.Text.Json.Serialization;

namespace WpfDevTools.Shared.ErrorHandling;

/// <summary>
/// JSON-serializable error payload shared by MCP server and Inspector analyzers.
/// </summary>
public sealed class ToolErrorPayload
{
    /// <summary>
    /// Always false for error payloads.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success => false;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    /// <summary>
    /// Stable machine-readable error code.
    /// </summary>
    [JsonPropertyName("errorCode")]
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Recovery guidance for human and AI clients.
    /// </summary>
    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; init; }

    /// <summary>
    /// Optional structured context for advanced recovery logic.
    /// </summary>
    [JsonPropertyName("errorData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ErrorData { get; init; }
}
