namespace WpfDevTools.Inspector.Events;

internal sealed record WatchEventRecord(
    string EventType,
    DateTimeOffset TimestampUtc,
    string SourceKey,
    string ElementId,
    string? PropertyName,
    string? EventName,
    string? NewValue,
    string? ValueType,
    string? SenderType,
    string? SenderName,
    string? RoutingStrategy,
    bool? Handled,
    string? OriginalSourceType)
{
    public bool PayloadTruncated { get; init; }

    public WatchEventTruncationMetadata? TruncationMetadata { get; init; }
}

internal sealed record WatchEventTruncationMetadata(
    int MaxStringLength,
    IReadOnlyList<string> Reasons,
    IReadOnlyDictionary<string, int> OriginalStringLengths);
