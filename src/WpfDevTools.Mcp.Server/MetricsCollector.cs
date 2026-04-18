namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Collects performance metrics for MCP server requests
/// Thread-safe metrics collection with percentile tracking
/// </summary>
public sealed class MetricsCollector
{
    private readonly object _lock = new();
    private long _totalRequests;
    private long _successCount;
    private long _errorCount;
    private readonly CircularBuffer<long> _latencies;
    private long _totalLatency;
    private readonly Dictionary<string, MethodMetrics> _methodMetrics = new();

    private const int MaxLatencySamples = 1000;

    /// <summary>
    /// Initializes a new instance of the MetricsCollector class
    /// </summary>
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

            // Track per-method metrics
            if (!_methodMetrics.TryGetValue(method, out var methodStats))
            {
                methodStats = new MethodMetrics();
                _methodMetrics[method] = methodStats;
            }
            methodStats.TotalCalls++;
            methodStats.TotalLatency += latencyMs;
            if (!success) methodStats.ErrorCount++;
        }
    }

    /// <summary>
    /// Get an immutable snapshot of current metrics
    /// </summary>
    public MetricsSnapshot GetSnapshot()
    {
        long[] latencyArray;
        long totalRequests, successCount, errorCount, totalLatency;
        Dictionary<string, MethodMetricsSnapshot> methodSnapshots;

        lock (_lock)
        {
            totalRequests = _totalRequests;
            successCount = _successCount;
            errorCount = _errorCount;
            totalLatency = _totalLatency;

            latencyArray = _latencies.ToArray();

            methodSnapshots = _methodMetrics.ToDictionary(
                kvp => kvp.Key,
                kvp => new MethodMetricsSnapshot
                {
                    TotalCalls = kvp.Value.TotalCalls,
                    ErrorCount = kvp.Value.ErrorCount,
                    AverageLatency = kvp.Value.TotalCalls > 0
                        ? (double)kvp.Value.TotalLatency / kvp.Value.TotalCalls
                        : 0
                });
        }

        // Sort and percentile calculation outside lock to reduce contention
        Array.Sort(latencyArray);

        return new MetricsSnapshot
        {
            TotalRequests = totalRequests,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            ErrorRate = totalRequests > 0 ? (double)errorCount / totalRequests : 0,
            AverageLatency = totalRequests > 0 ? (double)totalLatency / totalRequests : 0,
            P50Latency = CalculatePercentile(latencyArray, 0.50),
            P95Latency = CalculatePercentile(latencyArray, 0.95),
            P99Latency = CalculatePercentile(latencyArray, 0.99),
            MethodMetrics = methodSnapshots
        };
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
            _methodMetrics.Clear();
        }
    }

    private class MethodMetrics
    {
        public long TotalCalls;
        public long ErrorCount;
        public long TotalLatency;
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

        public T[] ToArray()
        {
            if (_count == 0)
                return Array.Empty<T>();

            var result = new T[_count];
            int start = _count < _buffer.Length ? 0 : _head;

            if (start + _count <= _buffer.Length)
            {
                Array.Copy(_buffer, start, result, 0, _count);
            }
            else
            {
                var firstPart = _buffer.Length - start;
                Array.Copy(_buffer, start, result, 0, firstPart);
                Array.Copy(_buffer, 0, result, firstPart, _count - firstPart);
            }

            return result;
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
public sealed record MetricsSnapshot
{
    /// <summary>
    /// Gets the total number of requests processed
    /// </summary>
    public long TotalRequests { get; init; }

    /// <summary>
    /// Gets the number of successful requests
    /// </summary>
    public long SuccessCount { get; init; }

    /// <summary>
    /// Gets the number of failed requests
    /// </summary>
    public long ErrorCount { get; init; }

    /// <summary>
    /// Gets the error rate (0.0 to 1.0)
    /// </summary>
    public double ErrorRate { get; init; }

    /// <summary>
    /// Gets the average latency in milliseconds
    /// </summary>
    public double AverageLatency { get; init; }

    /// <summary>
    /// Gets the 50th percentile (median) latency in milliseconds
    /// </summary>
    public double P50Latency { get; init; }

    /// <summary>
    /// Gets the 95th percentile latency in milliseconds
    /// </summary>
    public double P95Latency { get; init; }

    /// <summary>
    /// Gets the 99th percentile latency in milliseconds
    /// </summary>
    public double P99Latency { get; init; }

    /// <summary>
    /// Gets per-method (tool) metrics for observability
    /// </summary>
    public IReadOnlyDictionary<string, MethodMetricsSnapshot> MethodMetrics { get; init; }
        = new Dictionary<string, MethodMetricsSnapshot>();
}

/// <summary>
/// Immutable snapshot of per-method metrics
/// </summary>
public sealed record MethodMetricsSnapshot
{
    /// <summary>
    /// Total calls to this method
    /// </summary>
    public long TotalCalls { get; init; }

    /// <summary>
    /// Number of errors for this method
    /// </summary>
    public long ErrorCount { get; init; }

    /// <summary>
    /// Average latency for this method in milliseconds
    /// </summary>
    public double AverageLatency { get; init; }
}
