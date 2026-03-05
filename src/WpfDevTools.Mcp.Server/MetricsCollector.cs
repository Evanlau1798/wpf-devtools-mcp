namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Collects performance metrics for MCP server requests
/// Thread-safe metrics collection with percentile tracking
/// </summary>
public class MetricsCollector
{
    private readonly object _lock = new();
    private long _totalRequests;
    private long _successCount;
    private long _errorCount;
    private readonly CircularBuffer<long> _latencies;
    private long _totalLatency;

    private const int MaxLatencySamples = 1000;

    public MetricsCollector()
    {
        _latencies = new CircularBuffer<long>(MaxLatencySamples);
    }

    /// <summary>
    /// Record a request with its latency and success status
    /// </summary>
    public void RecordRequest(string method, long latencyMs, bool success)
    {
        lock (_lock)
        {
            _totalRequests++;
            _totalLatency += latencyMs;

            if (success)
                _successCount++;
            else
                _errorCount++;

            _latencies.Add(latencyMs);
        }
    }

    /// <summary>
    /// Get an immutable snapshot of current metrics
    /// </summary>
    public MetricsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var latencyArray = _latencies.GetItems().ToArray();
            Array.Sort(latencyArray);

            return new MetricsSnapshot
            {
                TotalRequests = _totalRequests,
                SuccessCount = _successCount,
                ErrorCount = _errorCount,
                ErrorRate = _totalRequests > 0 ? (double)_errorCount / _totalRequests : 0,
                AverageLatency = _totalRequests > 0 ? (double)_totalLatency / _totalRequests : 0,
                P50Latency = CalculatePercentile(latencyArray, 0.50),
                P95Latency = CalculatePercentile(latencyArray, 0.95),
                P99Latency = CalculatePercentile(latencyArray, 0.99)
            };
        }
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _totalRequests = 0;
            _successCount = 0;
            _errorCount = 0;
            _totalLatency = 0;
            _latencies.Clear();
        }
    }

    private static double CalculatePercentile(long[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
            return 0;

        if (sortedValues.Length == 1)
            return sortedValues[0];

        var index = percentile * (sortedValues.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
            return sortedValues[lower];

        var fraction = index - lower;
        return sortedValues[lower] * (1 - fraction) + sortedValues[upper] * fraction;
    }

    /// <summary>
    /// Circular buffer for storing latency samples
    /// </summary>
    private class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _head;
        private int _count;

        public CircularBuffer(int capacity)
        {
            _buffer = new T[capacity];
        }

        public void Add(T item)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;

            if (_count < _buffer.Length)
                _count++;
        }

        public IEnumerable<T> GetItems()
        {
            if (_count == 0)
                yield break;

            int start = _count < _buffer.Length ? 0 : _head;

            for (int i = 0; i < _count; i++)
            {
                yield return _buffer[(start + i) % _buffer.Length];
            }
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }
    }
}

/// <summary>
/// Immutable snapshot of metrics at a point in time
/// </summary>
public class MetricsSnapshot
{
    public long TotalRequests { get; init; }
    public long SuccessCount { get; init; }
    public long ErrorCount { get; init; }
    public double ErrorRate { get; init; }
    public double AverageLatency { get; init; }
    public double P50Latency { get; init; }
    public double P95Latency { get; init; }
    public double P99Latency { get; init; }
}
