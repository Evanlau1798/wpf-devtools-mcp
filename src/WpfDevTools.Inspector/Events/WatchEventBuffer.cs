using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace WpfDevTools.Inspector.Events;

internal sealed class WatchEventBuffer
{
    internal const int MaxPayloadStringLength = 1024;

    private readonly object _lock = new();
    private readonly int _capacity;
    private readonly WatchEventDeduplicator? _deduplicator;
    private readonly LinkedList<WatchEventRecord> _events = new();
    private readonly Dictionary<string, LinkedListNode<WatchEventRecord>> _dedupIndex = new(StringComparer.Ordinal);
    private int _droppedCount;

    public WatchEventBuffer(int capacity = 1024, WatchEventDeduplicator? deduplicator = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _capacity = capacity;
        _deduplicator = deduplicator;
    }

    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }

    public int DroppedCount
    {
        get
        {
            lock (_lock)
            {
                return _droppedCount;
            }
        }
    }

    public void Enqueue(WatchEventRecord record)
    {
        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        lock (_lock)
        {
            record = BoundPayloadStrings(record);
            var dedupKey = _deduplicator?.GetKey(record);
            if (dedupKey != null && _dedupIndex.TryGetValue(dedupKey, out var existingNode))
            {
                _events.Remove(existingNode);
                _dedupIndex.Remove(dedupKey);
            }

            var node = _events.AddLast(record);
            if (dedupKey != null)
            {
                _dedupIndex[dedupKey] = node;
            }

            while (_events.Count > _capacity)
            {
                RemoveFirstAndTrackDrop();
            }
        }
    }

    private static WatchEventRecord BoundPayloadStrings(WatchEventRecord record)
    {
        var truncation = new PayloadTruncationBuilder();
        var bounded = record with
        {
            EventType = truncation.LimitRequired("eventType", record.EventType),
            SourceKey = truncation.LimitRequired("sourceKey", record.SourceKey),
            ElementId = truncation.LimitRequired("elementId", record.ElementId),
            PropertyName = truncation.Limit("propertyName", record.PropertyName),
            EventName = truncation.Limit("eventName", record.EventName),
            NewValue = truncation.Limit("newValue", record.NewValue),
            ValueType = truncation.Limit("valueType", record.ValueType),
            SenderType = truncation.Limit("senderType", record.SenderType),
            SenderName = truncation.Limit("senderName", record.SenderName),
            RoutingStrategy = truncation.Limit("routingStrategy", record.RoutingStrategy),
            OriginalSourceType = truncation.Limit("originalSourceType", record.OriginalSourceType)
        };

        return truncation.Truncated
            ? bounded with
            {
                PayloadTruncated = true,
                TruncationMetadata = truncation.ToMetadata()
            }
            : bounded;
    }

    public IReadOnlyList<WatchEventRecord> GetSnapshot()
    {
        lock (_lock)
        {
            return _events.ToList();
        }
    }

    public IReadOnlyList<WatchEventRecord> Drain(
        int maxEvents = int.MaxValue,
        IReadOnlyCollection<string>? eventTypes = null,
        string? elementId = null,
        DateTimeOffset? sinceTimestamp = null)
    {
        if (maxEvents <= 0)
        {
            return Array.Empty<WatchEventRecord>();
        }

        lock (_lock)
        {
            var drained = new List<WatchEventRecord>(Math.Min(maxEvents, _events.Count));
            var eventTypeSet = eventTypes is { Count: > 0 }
                ? new HashSet<string>(eventTypes, StringComparer.Ordinal)
                : null;
            var node = _events.First;

            while (drained.Count < maxEvents && node is not null)
            {
                var next = node.Next;
                if (MatchesFilters(node.Value, eventTypeSet, elementId, sinceTimestamp))
                {
                    _events.Remove(node);
                    RemoveDedupIndex(node.Value);
                    drained.Add(node.Value);
                }

                node = next;
            }

            return drained;
        }
    }

    public int ConsumeDroppedCount()
    {
        lock (_lock)
        {
            var droppedCount = _droppedCount;
            _droppedCount = 0;
            return droppedCount;
        }
    }

    private void RemoveFirstAndTrackDrop()
    {
        if (_events.First is not { } node)
        {
            return;
        }

        _events.RemoveFirst();
        RemoveDedupIndex(node.Value);
        _droppedCount++;
    }

    private void RemoveDedupIndex(WatchEventRecord record)
    {
        var dedupKey = _deduplicator?.GetKey(record);
        if (dedupKey != null)
        {
            _dedupIndex.Remove(dedupKey);
        }
    }

    private static bool MatchesFilters(
        WatchEventRecord record,
        HashSet<string>? eventTypes,
        string? elementId,
        DateTimeOffset? sinceTimestamp)
    {
        if (eventTypes != null && !eventTypes.Contains(record.EventType))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(elementId)
            && !string.Equals(record.ElementId, elementId, StringComparison.Ordinal))
        {
            return false;
        }

        if (sinceTimestamp is { } cutoff && record.TimestampUtc < cutoff)
        {
            return false;
        }

        return true;
    }

    private sealed class PayloadTruncationBuilder
    {
        private readonly Dictionary<string, int> _originalStringLengths = new(StringComparer.Ordinal);

        public bool Truncated => _originalStringLengths.Count > 0;

        public string? Limit(string propertyName, string? value)
        {
            if (value == null || value.Length <= MaxPayloadStringLength)
            {
                return value;
            }

            _originalStringLengths[propertyName] = value.Length;
            return TruncateWithHash(value);
        }

        public WatchEventTruncationMetadata ToMetadata() => new(
            MaxPayloadStringLength,
            new[] { "PayloadStringLength" },
            new Dictionary<string, int>(_originalStringLengths, StringComparer.Ordinal));

        private static string TruncateWithHash(string value)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];
            var suffix = $"...#{hash}";
            var prefixLength = Math.Max(0, MaxPayloadStringLength - suffix.Length);
            return value[..prefixLength] + suffix;
        }

        public string LimitRequired(string propertyName, string value) =>
            Limit(propertyName, value) ?? string.Empty;
    }
}
