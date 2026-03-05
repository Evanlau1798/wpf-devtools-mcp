namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Fixed-size circular buffer with O(1) add and O(1) space complexity
/// Used for storing frame times without the O(n) cost of List.RemoveAt(0)
/// </summary>
internal class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be positive", nameof(capacity));

        _buffer = new T[capacity];
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Add item to buffer (overwrites oldest if full)
    /// O(1) operation
    /// </summary>
    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;

        if (_count < _buffer.Length)
            _count++;
    }

    /// <summary>
    /// Get all items in insertion order
    /// </summary>
    public IEnumerable<T> GetItems()
    {
        if (_count == 0)
            yield break;

        // Start from oldest item
        int start = _count < _buffer.Length ? 0 : _head;

        for (int i = 0; i < _count; i++)
        {
            yield return _buffer[(start + i) % _buffer.Length];
        }
    }

    /// <summary>
    /// Number of items currently stored
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Clear all items
    /// </summary>
    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _head = 0;
        _count = 0;
    }
}
