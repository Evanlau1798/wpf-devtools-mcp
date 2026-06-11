namespace WpfDevTools.Inspector.Events;

internal sealed class WatchEventDeduplicator
{
    public string? GetKey(WatchEventRecord record)
    {
        if (record.EventType != "DpChange")
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(record.SourceKey)
            ? null
            : record.SourceKey;
    }
}
