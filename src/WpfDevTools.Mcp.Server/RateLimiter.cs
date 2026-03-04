namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Rate limiter to prevent DoS attacks via rapid tool invocations
/// Uses token bucket algorithm for smooth rate limiting
/// </summary>
public class RateLimiter
{
    private readonly int _maxTokens;
    private readonly TimeSpan _refillInterval;
    private int _tokens;
    private DateTime _lastRefill;
    private readonly object _lock = new object();

    /// <summary>
    /// Create a rate limiter
    /// </summary>
    /// <param name="maxRequestsPerInterval">Maximum requests allowed per interval</param>
    /// <param name="interval">Time interval for rate limiting</param>
    public RateLimiter(int maxRequestsPerInterval, TimeSpan interval)
    {
        _maxTokens = maxRequestsPerInterval;
        _refillInterval = interval;
        _tokens = maxRequestsPerInterval;
        _lastRefill = DateTime.UtcNow;
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
            _lastRefill = DateTime.UtcNow;
        }
    }

    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastRefill;

        if (elapsed >= _refillInterval)
        {
            // Refill tokens based on elapsed intervals
            var intervalsElapsed = (int)(elapsed.TotalMilliseconds / _refillInterval.TotalMilliseconds);
            var tokensToAdd = intervalsElapsed * _maxTokens;

            _tokens = Math.Min(_tokens + tokensToAdd, _maxTokens);
            _lastRefill = now;
        }
    }
}

/// <summary>
/// Rate limiter manager for multiple sessions
/// </summary>
public class RateLimiterManager
{
    private readonly Dictionary<int, RateLimiter> _limiters = new();
    private readonly object _lock = new object();
    private readonly int _maxRequestsPerMinute;
    private readonly TimeSpan _interval;

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
            if (!_limiters.TryGetValue(processId, out var limiter))
            {
                limiter = new RateLimiter(_maxRequestsPerMinute, _interval);
                _limiters[processId] = limiter;
            }

            return limiter.TryAcquire();
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
            if (_limiters.TryGetValue(processId, out var limiter))
            {
                return limiter.GetAvailableTokens();
            }
            return _maxRequestsPerMinute;
        }
    }
}
