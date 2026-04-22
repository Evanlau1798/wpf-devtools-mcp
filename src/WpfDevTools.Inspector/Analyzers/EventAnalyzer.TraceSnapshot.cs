using WpfDevTools.Inspector.Events;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class EventAnalyzer
{
    private IReadOnlyList<object> GetTraceSnapshotEvents(TraceSessionHandle? requestedSession, string? requestedEventName)
    {
        var traceMetadata = requestedSession?.Metadata ?? _lastTraceMetadata;
        var isCurrentSession = requestedSession == null
            ? _activeTraceSession != null
            : _activeTraceSession != null
                && ReferenceEquals(_activeTraceSession.TokenSource, requestedSession.TokenSource)
                && ReferenceEquals(_activeTraceSession.Metadata, requestedSession.Metadata);

        if (!IsRequestedEventMatch(traceMetadata, requestedEventName))
        {
            return Array.Empty<object>();
        }

        if (_eventTrace.Count > 0 && isCurrentSession)
        {
            return _eventTrace.GetSnapshot();
        }

        if (_watchEventBuffer == null || traceMetadata == null)
        {
            return Array.Empty<object>();
        }

        var windowEndUtc = traceMetadata.StartedAtUtc.AddMilliseconds(traceMetadata.EffectiveDurationMs);
        return _watchEventBuffer.GetSnapshot()
            .Where(record => IsMatchingTraceBufferRecord(record, traceMetadata, windowEndUtc))
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
            && record.SourceKey.StartsWith($"event:{traceMetadata.SessionId}:", StringComparison.Ordinal)
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
