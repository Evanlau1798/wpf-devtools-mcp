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
    /// Compatibility alias for dynamic callers expecting lowercase property names.
    /// </summary>
    [JsonIgnore]
    public bool success => Success;

    /// <summary>
    /// Compatibility alias for dynamic callers expecting lowercase property names.
    /// </summary>
    [JsonIgnore]
    public string error => Error;

    /// <summary>
    /// Stable machine-readable error code.
    /// </summary>
    [JsonPropertyName("errorCode")]
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Compatibility alias for dynamic callers expecting lowercase property names.
    /// </summary>
    [JsonIgnore]
    public string errorCode => ErrorCode;

    /// <summary>
    /// Recovery guidance for human and AI clients.
    /// </summary>
    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; init; }

    /// <summary>
    /// Compatibility alias for dynamic callers expecting lowercase property names.
    /// </summary>
    [JsonIgnore]
    public string? hint => Hint;

    /// <summary>
    /// Optional structured context for advanced recovery logic.
    /// </summary>
    [JsonPropertyName("errorData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ErrorData { get; init; }

    /// <summary>
    /// Canonical machine-readable recovery guidance for automated clients.
    /// </summary>
    [JsonPropertyName("recovery")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolErrorRecovery? Recovery { get; init; }

    /// <summary>
    /// Deterministic next-step guidance projected for backward-compatible callers.
    /// </summary>
    [JsonPropertyName("suggestedAction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuggestedAction { get; init; }

    /// <summary>
    /// Indicates that the current session should be reconnected before retrying.
    /// </summary>
    [JsonPropertyName("requiresReconnect")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequiresReconnect { get; init; }

    /// <summary>
    /// Indicates that runtime state may have changed after the timeout.
    /// </summary>
    [JsonPropertyName("stateAfterTimeoutUnknown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StateAfterTimeoutUnknown { get; init; }

    /// <summary>
    /// Target process identifier associated with the recovery guidance.
    /// </summary>
    [JsonPropertyName("processId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessId { get; init; }

    /// <summary>
    /// Timeout budget associated with the failed operation.
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Backward-compatible top-level projection used by event analyzer callers.
    /// </summary>
    [JsonPropertyName("availableEvents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AvailableEvents { get; init; }

    /// <summary>
    /// Compatibility alias for dynamic callers expecting lowercase property names.
    /// </summary>
    [JsonIgnore]
    public object? errorData => ErrorData;
}
