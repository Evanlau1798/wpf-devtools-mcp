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
    string? OriginalSourceType);
