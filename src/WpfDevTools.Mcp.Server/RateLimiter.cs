using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Rate limiter to prevent DoS attacks via rapid tool invocations
/// Uses token bucket algorithm for smooth rate limiting
/// </summary>
public readonly record struct RateLimitStatus(bool Allowed, int AvailableTokens, TimeSpan RetryAfter);

/// <summary>
/// Optional extension surface for callers that need an atomic post-acquire snapshot.
/// </summary>
public interface IRateLimiterStatusProvider
{
    /// <summary>
    /// Attempt to acquire a request token and return the resulting rate-limit snapshot.
    /// </summary>
    RateLimitStatus TryAcquireWithStatus(int processId);
}

/// <summary>
/// Rate limiter to prevent DoS attacks via rapid tool invocations
/// Uses token bucket algorithm for smooth rate limiting
/// </summary>
public sealed class RateLimiter
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
    /// <param name="timeProvider">Optional time provider for testing (defaults to DateTime.UtcNow)</param>
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
            return TryAcquireWithStatusCore().Allowed;
        }
    }

    /// <summary>
    /// Try to acquire a token and return the resulting rate-limit snapshot.
    /// </summary>
    public RateLimitStatus TryAcquireWithStatus()
    {
        lock (_lock)
        {
            return TryAcquireWithStatusCore();
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
    /// Get the remaining wait time until the next refill window when no tokens are available.
    /// Returns TimeSpan.Zero when a request can proceed immediately.
    /// </summary>
    public TimeSpan GetRetryAfter()
    {
        lock (_lock)
        {
            RefillTokens();
            if (_tokens > 0)
            {
                return TimeSpan.Zero;
            }

            return GetRetryAfterCore();
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
            var intervalsElapsed = (long)(elapsed.TotalMilliseconds / _refillInterval.TotalMilliseconds);
            var tokensToAdd = (int)Math.Min(intervalsElapsed * _maxTokens, _maxTokens);

            _tokens = Math.Min(_tokens + tokensToAdd, _maxTokens);

            // CRITICAL FIX: Update _lastRefill based on actual intervals elapsed
            // This ensures consistent refill timing even with time drift
            // Example: if 2.5 intervals passed, advance by exactly 2 intervals
            _lastRefill = _lastRefill.Add(TimeSpan.FromTicks((long)intervalsElapsed * _refillInterval.Ticks));
        }
    }

    private RateLimitStatus TryAcquireWithStatusCore()
    {
        RefillTokens();

        if (_tokens > 0)
        {
            _tokens--;
            return new RateLimitStatus(true, _tokens, TimeSpan.Zero);
        }

        return new RateLimitStatus(false, _tokens, GetRetryAfterCore());
    }

    private TimeSpan GetRetryAfterCore()
    {
        var nextRefillAt = _lastRefill.Add(_refillInterval);
        var remaining = nextRefillAt - _timeProvider();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
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

    /// <summary>
    /// Get the remaining wait time until the session can issue another request.
    /// Returns TimeSpan.Zero when a request can proceed immediately.
    /// </summary>
    TimeSpan GetRetryAfter(int processId);
}

/// <summary>
/// Rate limiter manager for multiple sessions
/// </summary>
public sealed class RateLimiterManager : IRateLimiterManager, IRateLimiterStatusProvider, IDisposable
{
    // INTENTIONAL DEVIATION from project immutability principle:
    // LastAccessed is mutated in-place to avoid allocating a new entry on every TryAcquire() call.
    // TryAcquire() is called on every tool invocation (~100/min/session), so minimizing
    // GC pressure here is a justified performance trade-off. Access is serialized by _lock.
    private sealed class RateLimiterEntry
    {
        public RateLimiter Limiter { get; }
        public DateTimeOffset LastAccessed { get; set; }

        public RateLimiterEntry(RateLimiter limiter)
        {
            Limiter = limiter;
            LastAccessed = DateTimeOffset.UtcNow;
        }
    }

    private readonly Dictionary<int, RateLimiterEntry> _limiters = new();

    // P-1: This single manager lock is intentional for the current server shape.
    // Active sessions are capped by McpServerConfiguration.MaxSessions, the hot path
    // performs a short dictionary lookup plus a per-session RateLimiter call, and
    // eviction is bounded by MaxEntries. A local Release smoke probe at the current
    // session cap processed 100,000 TryAcquire calls in 27.6 ms; keep this simple
    // until profiling shows material contention under realistic concurrent MCP calls.
    private readonly object _lock = new object();
    private readonly int _maxRequestsPerMinute;
    private readonly TimeSpan _interval;
    private readonly System.Threading.Timer _cleanupTimer;
    private int _disposeState;
    private const int MaxEntries = 1000;

    /// <summary>
    /// Create rate limiter manager
    /// </summary>
    /// <param name="maxRequestsPerMinute">Maximum requests per minute per session</param>
    public RateLimiterManager(int maxRequestsPerMinute = 100)
    {
        _maxRequestsPerMinute = maxRequestsPerMinute;
        _interval = TimeSpan.FromMinutes(1);
        _cleanupTimer = new System.Threading.Timer(
            _ => RateLimiterCleanupGuard.Execute(
                Volatile.Read(ref _disposeState) != 0,
                cleanupAction: () => RemoveStaleEntries(TimeSpan.FromMinutes(30)),
                onError: ex => System.Diagnostics.Debug.WriteLine(
                    $"RateLimiterManager cleanup failed: {SensitiveLogRedactor.Redact(ex.Message)}")),
            null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
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
            entry.LastAccessed = DateTimeOffset.UtcNow;
            return entry.Limiter.TryAcquire();
        }
    }

    /// <summary>
    /// Try to acquire permission for a request and return the resulting rate-limit snapshot.
    /// </summary>
    public RateLimitStatus TryAcquireWithStatus(int processId)
    {
        lock (_lock)
        {
            if (!_limiters.TryGetValue(processId, out var entry))
            {
                if (_limiters.Count >= MaxEntries)
                {
                    EvictOldestEntries(_limiters.Count - MaxEntries + 1);
                }

                var limiter = new RateLimiter(_maxRequestsPerMinute, _interval);
                entry = new RateLimiterEntry(limiter);
                _limiters[processId] = entry;
                return limiter.TryAcquireWithStatus();
            }

            entry.LastAccessed = DateTimeOffset.UtcNow;
            return entry.Limiter.TryAcquireWithStatus();
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
    /// Get the remaining wait time until the session can issue another request.
    /// </summary>
    public TimeSpan GetRetryAfter(int processId)
    {
        lock (_lock)
        {
            if (_limiters.TryGetValue(processId, out var entry))
            {
                return entry.Limiter.GetRetryAfter();
            }

            return TimeSpan.Zero;
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
            var cutoff = DateTimeOffset.UtcNow - staleDuration;
            var staleKeys = new List<int>();
            foreach (var kvp in _limiters)
            {
                if (kvp.Value.LastAccessed < cutoff)
                    staleKeys.Add(kvp.Key);
            }

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
        if (_limiters.Count == 0 || count <= 0)
            return;

        var candidateCount = Math.Min(count, _limiters.Count);
        var candidates = new PriorityQueue<(int Key, DateTimeOffset LastAccessed), long>(candidateCount);
        foreach (var kvp in _limiters)
        {
            var lastAccessed = kvp.Value.LastAccessed;
            var priority = -lastAccessed.UtcTicks;

            if (candidates.Count < candidateCount)
            {
                candidates.Enqueue((kvp.Key, lastAccessed), priority);
                continue;
            }

            candidates.TryPeek(out var newestCandidate, out _);
            if (lastAccessed < newestCandidate.LastAccessed)
            {
                candidates.Dequeue();
                candidates.Enqueue((kvp.Key, lastAccessed), priority);
            }
        }

        while (candidates.TryDequeue(out var candidate, out _))
            _limiters.Remove(candidate.Key);
    }

    /// <summary>
    /// Dispose the cleanup timer
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            return;

        _cleanupTimer?.Dispose();
    }
}
