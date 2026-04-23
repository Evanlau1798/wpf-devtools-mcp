namespace WpfDevTools.Inspector.Analyzers;

internal sealed class TraceEventRingBuffer
{
    private readonly object?[] _items;
    private int _head;
    private int _count;

    public TraceEventRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _items = new object[capacity];
    }

    public int Count => _count;

    public void Add(object item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (_count < _items.Length)
        {
            _items[(_head + _count) % _items.Length] = item;
            _count++;
            return;
        }

        _items[_head] = item;
        _head = (_head + 1) % _items.Length;
    }

    public void Clear()
    {
        for (var index = 0; index < _count; index++)
        {
            _items[(_head + index) % _items.Length] = null;
        }

        _head = 0;
        _count = 0;
    }

    public IReadOnlyList<object> GetSnapshot()
    {
        if (_count == 0)
        {
            return Array.Empty<object>();
        }

        var snapshot = new List<object>(_count);
        for (var index = 0; index < _count; index++)
        {
            var item = _items[(_head + index) % _items.Length]
                ?? throw new InvalidOperationException("Trace event ring buffer contained an unexpected empty slot.");
            snapshot.Add(item);
        }

        return snapshot;
    }
}