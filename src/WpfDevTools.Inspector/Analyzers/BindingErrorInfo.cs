namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Immutable class representing a captured WPF data binding error.
/// Uses init-only properties for immutability.
/// </summary>
public sealed class BindingErrorInfo
{
    /// <summary>
    /// UTC timestamp when the error was captured
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// The binding error message from WPF trace system
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The trace event type (e.g., Error, Warning)
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// The trace source event ID
    /// </summary>
    public int SourceId { get; init; }
}
