using WpfDevTools.Inspector.Events;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class EventAnalyzer
{
    private IReadOnlyList<object> GetTraceSnapshotEvents(string? requestedEventName)
    {
        if (_eventTrace.Count > 0)
        {
            return _eventTrace.ToList();
        }

        if (_watchEventBuffer == null || _lastTraceMetadata == null)
        {
            return Array.Empty<object>();
        }

        if (!string.IsNullOrWhiteSpace(requestedEventName)
            && !string.Equals(requestedEventName, _lastTraceMetadata.EventName, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<object>();
        }

        var windowEndUtc = _lastTraceMetadata.StartedAtUtc.AddMilliseconds(_lastTraceMetadata.EffectiveDurationMs);
        return _watchEventBuffer.GetSnapshot()
            .Where(record => IsMatchingTraceBufferRecord(record, _lastTraceMetadata, windowEndUtc))
            .Select(CreateTraceSnapshotEvent)
            .Cast<object>()
            .ToList();
    }

    private static bool IsMatchingTraceBufferRecord(
        WatchEventRecord record,
        TraceSessionMetadata traceMetadata,
        DateTimeOffset windowEndUtc)
    {
        return string.Equals(record.EventType, "RoutedEvent", StringComparison.Ordinal)
            && string.Equals(record.ElementId, traceMetadata.ElementId, StringComparison.Ordinal)
            && string.Equals(record.EventName, traceMetadata.EventName, StringComparison.Ordinal)
            && record.TimestampUtc >= traceMetadata.StartedAtUtc
            && record.TimestampUtc <= windowEndUtc;
    }

    private static object CreateTraceSnapshotEvent(WatchEventRecord record)
    {
        return new
        {
            timestamp = record.TimestampUtc.UtcDateTime,
            sender = record.SenderType,
            senderName = record.SenderName,
            eventName = record.EventName,
            routingStrategy = record.RoutingStrategy,
            handled = record.Handled,
            originalSource = record.OriginalSourceType
        };
    }
}
