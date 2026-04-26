using System.Text.Json.Serialization;

namespace WpfDevTools.Shared.ErrorHandling;

/// <summary>
/// Canonical machine-readable recovery surface for MCP and Inspector tool errors.
/// </summary>
public sealed class ToolErrorRecovery
{
    /// <summary>
    /// Human-readable recovery hint.
    /// </summary>
    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; init; }

    /// <summary>
    /// Deterministic next-step guidance for retry or recovery.
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
    /// Numeric backoff window before retrying.
    /// </summary>
    [JsonPropertyName("retryAfterSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryAfterSeconds { get; init; }

    /// <summary>
    /// Human-readable backoff guidance.
    /// </summary>
    [JsonPropertyName("retryAfter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RetryAfter { get; init; }

    /// <summary>
    /// Remaining tokens for throttled operations when available.
    /// </summary>
    [JsonPropertyName("availableTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AvailableTokens { get; init; }

    /// <summary>
    /// Enumerated event names or similar recovery options.
    /// </summary>
    [JsonPropertyName("availableEvents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AvailableEvents { get; init; }
}
