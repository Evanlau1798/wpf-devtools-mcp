namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Information about a WPF window in the target application
/// </summary>
public sealed class WindowInfo
{
    /// <summary>
    /// Zero-based index in Application.Current.Windows
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Window title
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Window type name
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Whether this window is currently active
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Element ID for this window (can be used as elementId in other tools)
    /// </summary>
    public string ElementId { get; init; } = string.Empty;
}
