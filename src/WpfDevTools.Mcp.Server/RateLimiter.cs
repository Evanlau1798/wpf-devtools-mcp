namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Rate limiter to prevent DoS attacks via rapid tool invocations
/// Uses token bucket algorithm for smooth rate limiting
/// </summary>
public class RateLimiter
{
    private readonly int _maxTokens;
    private readonly TimeSpan _refillInterval;
    private readonly Func<DateTime> _timeProvider;
    private int _tokens;
    private DateTime _lastRefill;
    private readonly object _lock = new object();

    /// <summary>
    /// Create a rate limiter
    /// </summary>
    /// <param name="maxRequestsPerInterval">Maximum requests allowed per interval</param>
    /// <param name="interval">Time interval for rate limiting</param>
    public RateLimiter(int maxRequestsPerInterval, TimeSpan interval, Func<DateTime>? timeProvider = null)
    {
        _maxTokens = maxRequestsPerInterval;
        _refillInterval = interval;
        _timeProvider = timeProvider ?? (() => DateTime.UtcNow);
        _tokens = maxRequestsPerInterval;
        _lastRefill = _timeProvider();
    }

    /// <summary>
    /// Try to acquire a token for request execution
    /// </summary>
    /// <returns>True if request is allowed, false if rate limit exceeded</returns>
    public bool TryAcquire()
    {
        lock (_lock)
        {
            RefillTokens();

            if (_tokens > 0)
            {
                _tokens--;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Get current token count (for monitoring)
    /// </summary>
    public int GetAvailableTokens()
    {
        lock (_lock)
        {
            RefillTokens();
            return _tokens;
        }
    }

    /// <summary>
    /// Reset rate limiter (for testing)
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _tokens = _maxTokens;
            _lastRefill = _timeProvider();
        }
    }

    private void RefillTokens()
    {
        // CRITICAL FIX: Cache DateTime.UtcNow to prevent time skew issues
        // If system clock changes between calls, calculations could be incorrect
        var now = _timeProvider();
        var elapsed = now - _lastRefill;

        if (elapsed >= _refillInterval)
        {
            // Refill tokens based on elapsed intervals
            var intervalsElapsed = (int)(elapsed.TotalMilliseconds / _refillInterval.TotalMilliseconds);
            var tokensToAdd = intervalsElapsed * _maxTokens;

            _tokens = Math.Min(_tokens + tokensToAdd, _maxTokens);

            // CRITICAL FIX: Update _lastRefill based on actual intervals elapsed
            // This ensures consistent refill timing even with time drift
            // Example: if 2.5 intervals passed, advance by exactly 2 intervals
            _lastRefill = _lastRefill.Add(TimeSpan.FromTicks(intervalsElapsed * _refillInterval.Ticks));
        }
    }
}

/// <summary>
/// Interface for rate limiter manager
/// </summary>
public interface IRateLimiterManager
{
    /// <summary>
    /// Try to acquire permission for a request
    /// </summary>
    /// <param name="processId">Process ID of the session</param>
    /// <returns>True if allowed, false if rate limit exceeded</returns>
    bool TryAcquire(int processId);

    /// <summary>
    /// Remove rate limiter for a session (when session ends)
    /// </summary>
    void RemoveSession(int processId);

    /// <summary>
    /// Get available tokens for a session (for monitoring)
    /// </summary>
    int GetAvailableTokens(int processId);
}

/// <summary>
/// Rate limiter manager for multiple sessions
/// </summary>
public class RateLimiterManager : IRateLimiterManager
{
    // CRITICAL FIX: Use mutable class instead of tuple to avoid allocations on every request
    private class RateLimiterEntry
    {
        public RateLimiter Limiter { get; }
        public DateTime LastAccessed { get; set; }

        public RateLimiterEntry(RateLimiter limiter)
        {
            Limiter = limiter;
            LastAccessed = DateTime.UtcNow;
        }
    }

    private readonly Dictionary<int, RateLimiterEntry> _limiters = new();
    private readonly object _lock = new object();
    private readonly int _maxRequestsPerMinute;
    private readonly TimeSpan _interval;
    private const int MaxEntries = 1000;

    /// <summary>
    /// Create rate limiter manager
    /// </summary>
    /// <param name="maxRequestsPerMinute">Maximum requests per minute per session</param>
    public RateLimiterManager(int maxRequestsPerMinute = 100)
    {
        _maxRequestsPerMinute = maxRequestsPerMinute;
        _interval = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Try to acquire permission for a request
    /// </summary>
    /// <param name="processId">Process ID of the session</param>
    /// <returns>True if allowed, false if rate limit exceeded</returns>
    public bool TryAcquire(int processId)
    {
        lock (_lock)
        {
            if (!_limiters.TryGetValue(processId, out var entry))
            {
                // Evict oldest entries if over limit before adding new one
                if (_limiters.Count >= MaxEntries)
                {
                    EvictOldestEntries(_limiters.Count - MaxEntries + 1);
                }

                var limiter = new RateLimiter(_maxRequestsPerMinute, _interval);
                entry = new RateLimiterEntry(limiter);
                _limiters[processId] = entry;
                return limiter.TryAcquire();
            }

            // CRITICAL FIX: Update LastAccessed without creating new tuple
            entry.LastAccessed = DateTime.UtcNow;
            return entry.Limiter.TryAcquire();
        }
    }

    /// <summary>
    /// Remove rate limiter for a session (when session ends)
    /// </summary>
    public void RemoveSession(int processId)
    {
        lock (_lock)
        {
            _limiters.Remove(processId);
        }
    }

    /// <summary>
    /// Get available tokens for a session (for monitoring)
    /// </summary>
    public int GetAvailableTokens(int processId)
    {
        lock (_lock)
        {
            if (_limiters.TryGetValue(processId, out var entry))
            {
                return entry.Limiter.GetAvailableTokens();
            }
            return _maxRequestsPerMinute;
        }
    }

    /// <summary>
    /// Remove entries that haven't been accessed within the specified duration
    /// </summary>
    /// <param name="staleDuration">Duration after which an entry is considered stale</param>
    /// <returns>Number of entries removed</returns>
    public int RemoveStaleEntries(TimeSpan staleDuration)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - staleDuration;
            var staleKeys = _limiters
                .Where(kvp => kvp.Value.LastAccessed < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _limiters.Remove(key);
            }

            return staleKeys.Count;
        }
    }

    /// <summary>
    /// Evict the oldest entries from the dictionary
    /// </summary>
    private void EvictOldestEntries(int count)
    {
        var keysToRemove = _limiters
            .OrderBy(kvp => kvp.Value.LastAccessed)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _limiters.Remove(key);
        }
    }
}
