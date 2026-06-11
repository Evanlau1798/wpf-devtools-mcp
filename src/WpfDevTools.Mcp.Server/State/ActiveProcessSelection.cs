namespace WpfDevTools.Mcp.Server.State;

/// <summary>
/// Tracks the explicitly selected active process for process-id omission workflows.
/// </summary>
internal sealed record ActiveProcessSelection
{
    /// <summary>
    /// Selected process identifier.
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// UTC timestamp when the selection was made.
    /// </summary>
    public required DateTimeOffset SelectedAtUtc { get; init; }
}
