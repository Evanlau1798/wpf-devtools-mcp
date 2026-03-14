using System.Collections.Generic;

namespace WpfDevTools.Inspector.Events;

internal sealed class WatchEventBuffer
{
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
        ArgumentNullException.ThrowIfNull(record);

        lock (_lock)
        {
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

    public IReadOnlyList<WatchEventRecord> GetSnapshot()
    {
        lock (_lock)
        {
            return _events.ToList();
        }
    }

    public IReadOnlyList<WatchEventRecord> Drain(int maxEvents = int.MaxValue)
    {
        if (maxEvents <= 0)
        {
            return Array.Empty<WatchEventRecord>();
        }

        lock (_lock)
        {
            var drained = new List<WatchEventRecord>(Math.Min(maxEvents, _events.Count));

            while (drained.Count < maxEvents && _events.First is { } node)
            {
                _events.RemoveFirst();
                RemoveDedupIndex(node.Value);
                drained.Add(node.Value);
            }

            return drained;
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
}
