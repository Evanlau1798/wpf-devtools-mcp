namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Immutable class representing a captured WPF data binding error.
/// Uses init-only properties for immutability.
/// </summary>
public sealed class BindingErrorInfo
{
    /// <summary>Error captured from WPF PresentationTraceSources.</summary>
    public const string OriginBindingTrace = "BindingTrace";

    /// <summary>Error detected from live BindingExpression status inspection.</summary>
    public const string OriginBindingExpression = "BindingExpression";
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

    /// <summary>
    /// Origin of the error for diagnostic classification.
    /// "BindingTrace" for errors from PresentationTraceSources,
    /// "BindingExpression" for errors from live BindingExpression inspection.
    /// </summary>
    public string Origin { get; init; } = OriginBindingTrace;
}
