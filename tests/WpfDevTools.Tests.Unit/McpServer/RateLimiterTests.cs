using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class RateLimiterTests
{
    [Fact]
    public void TryAcquire_WithinLimit_ShouldReturnTrue()
    {
        // Arrange
        var limiter = new RateLimiter(maxRequestsPerInterval: 5, interval: TimeSpan.FromMinutes(1));

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire().Should().BeTrue($"request {i + 1} should be allowed");
        }
    }

    [Fact]
    public void TryAcquire_ExceedingLimit_ShouldReturnFalse()
    {
        // Arrange
        var limiter = new RateLimiter(maxRequestsPerInterval: 3, interval: TimeSpan.FromMinutes(1));

        // Act - consume all tokens
        for (int i = 0; i < 3; i++)
        {
            limiter.TryAcquire().Should().BeTrue();
        }

        // Assert - next request should be denied
        limiter.TryAcquire().Should().BeFalse("rate limit exceeded");
    }

    [Fact]
    public void GetAvailableTokens_InitialState_ShouldReturnMaxTokens()
    {
        // Arrange
        var limiter = new RateLimiter(maxRequestsPerInterval: 10, interval: TimeSpan.FromMinutes(1));

        // Act
        var tokens = limiter.GetAvailableTokens();

        // Assert
        tokens.Should().Be(10);
    }

    [Fact]
    public void GetAvailableTokens_AfterAcquire_ShouldDecrement()
    {
        // Arrange
        var limiter = new RateLimiter(maxRequestsPerInterval: 10, interval: TimeSpan.FromMinutes(1));

        // Act
        limiter.TryAcquire();
        limiter.TryAcquire();
        var tokens = limiter.GetAvailableTokens();

        // Assert
        tokens.Should().Be(8);
    }

    [Fact]
    public void Reset_ShouldRestoreAllTokens()
    {
        // Arrange
        var limiter = new RateLimiter(maxRequestsPerInterval: 5, interval: TimeSpan.FromMinutes(1));
        limiter.TryAcquire();
        limiter.TryAcquire();

        // Act
        limiter.Reset();

        // Assert
        limiter.GetAvailableTokens().Should().Be(5);
    }

    [Fact]
    public void GetRetryAfter_WhenTokensRemain_ShouldReturnZero()
    {
        var currentTime = DateTime.UtcNow;
        var limiter = new RateLimiter(maxRequestsPerInterval: 2, interval: TimeSpan.FromMinutes(1), () => currentTime);

        limiter.TryAcquire().Should().BeTrue();

        limiter.GetRetryAfter().Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetRetryAfter_WhenLimitExceeded_ShouldReturnRemainingInterval()
    {
        var currentTime = DateTime.UtcNow;
        var limiter = new RateLimiter(maxRequestsPerInterval: 1, interval: TimeSpan.FromMinutes(1), () => currentTime);

        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeFalse();

        currentTime = currentTime.AddSeconds(43);

        limiter.GetRetryAfter().Should().Be(TimeSpan.FromSeconds(17));
    }

    [Fact]
    public void TryAcquireWithStatus_WhenLimitExceeded_ShouldReturnDeniedSnapshot()
    {
        var currentTime = DateTime.UtcNow;
        var limiter = new RateLimiter(maxRequestsPerInterval: 1, interval: TimeSpan.FromMinutes(1), () => currentTime);

        limiter.TryAcquireWithStatus().Allowed.Should().BeTrue();

        currentTime = currentTime.AddSeconds(43);
        var denied = limiter.TryAcquireWithStatus();

        denied.Allowed.Should().BeFalse();
        denied.AvailableTokens.Should().Be(0);
        denied.RetryAfter.Should().Be(TimeSpan.FromSeconds(17));
    }

    [Fact]
    public async Task TryAcquire_AfterIntervalElapsed_ShouldRefillTokens()
    {
        // Arrange
        var limiter = new RateLimiter(maxRequestsPerInterval: 5, interval: TimeSpan.FromMilliseconds(100));

        // Consume all tokens
        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire().Should().BeTrue();
        }
        limiter.TryAcquire().Should().BeFalse("all tokens consumed");

        // Act - wait for refill
        await Task.Delay(150);

        // Assert - tokens should be refilled
        limiter.TryAcquire().Should().BeTrue("tokens should be refilled after interval");
    }

    [Fact]
    public async Task GetAvailableTokens_AfterRefill_ShouldNotExceedMax()
    {
        // Arrange
        var limiter = new RateLimiter(maxRequestsPerInterval: 10, interval: TimeSpan.FromMilliseconds(50));
        limiter.TryAcquire(); // Use 1 token

        // Act - wait for multiple intervals
        await Task.Delay(200); // 4x the interval

        // Assert - should cap at max, not accumulate
        limiter.GetAvailableTokens().Should().Be(10, "tokens should not exceed max");
    }

    [Fact]
    public async Task TryAcquire_ConcurrentRequests_ShouldNotExceedLimit()
    {
        // Arrange
        var limiter = new RateLimiter(maxRequestsPerInterval: 10, interval: TimeSpan.FromMinutes(1));
        var successCount = 0;
        var lockObj = new object();

        // Act - simulate 100 concurrent requests
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() =>
            {
                if (limiter.TryAcquire())
                {
                    lock (lockObj)
                    {
                        successCount++;
                    }
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - only 10 should succeed
        successCount.Should().Be(10, "rate limiter should be thread-safe");
    }
}

public class RateLimiterManagerTests
{
    [Fact]
    public void TryAcquire_NewSession_ShouldCreateLimiter()
    {
        // Arrange
        var manager = new RateLimiterManager(maxRequestsPerMinute: 5);

        // Act
        var result = manager.TryAcquire(processId: 12345);

        // Assert
        result.Should().BeTrue("first request should be allowed");
    }

    [Fact]
    public void TryAcquire_ExceedingLimit_ShouldReturnFalse()
    {
        // Arrange
        var manager = new RateLimiterManager(maxRequestsPerMinute: 3);

        // Act - consume all tokens
        for (int i = 0; i < 3; i++)
        {
            manager.TryAcquire(12345).Should().BeTrue();
        }

        // Assert
        manager.TryAcquire(12345).Should().BeFalse("rate limit exceeded");
    }

    [Fact]
    public void TryAcquire_DifferentSessions_ShouldHaveIndependentLimits()
    {
        // Arrange
        var manager = new RateLimiterManager(maxRequestsPerMinute: 2);

        // Act - exhaust limit for process 1
        manager.TryAcquire(processId: 1).Should().BeTrue();
        manager.TryAcquire(processId: 1).Should().BeTrue();
        manager.TryAcquire(processId: 1).Should().BeFalse("process 1 limit exceeded");

        // Assert - process 2 should still have tokens
        manager.TryAcquire(processId: 2).Should().BeTrue("process 2 has independent limit");
    }

    [Fact]
    public void GetAvailableTokens_NewSession_ShouldReturnMaxTokens()
    {
        // Arrange
        var manager = new RateLimiterManager(maxRequestsPerMinute: 100);

        // Act
        var tokens = manager.GetAvailableTokens(processId: 12345);

        // Assert
        tokens.Should().Be(100);
    }

    [Fact]
    public void GetAvailableTokens_AfterAcquire_ShouldDecrement()
    {
        // Arrange
        var manager = new RateLimiterManager(maxRequestsPerMinute: 10);
        manager.TryAcquire(12345);
        manager.TryAcquire(12345);

        // Act
        var tokens = manager.GetAvailableTokens(12345);

        // Assert
        tokens.Should().Be(8);
    }

    [Fact]
    public void TryAcquireWithStatus_AfterLimitExceeded_ShouldReturnDeniedManagerSnapshot()
    {
        var manager = new RateLimiterManager(maxRequestsPerMinute: 1);

        manager.TryAcquireWithStatus(12345).Allowed.Should().BeTrue();

        var denied = manager.TryAcquireWithStatus(12345);

        denied.Allowed.Should().BeFalse();
        denied.AvailableTokens.Should().Be(0);
        denied.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void RemoveSession_ShouldCleanupLimiter()
    {
        // Arrange
        var manager = new RateLimiterManager(maxRequestsPerMinute: 5);
        manager.TryAcquire(12345);
        manager.TryAcquire(12345);

        // Act
        manager.RemoveSession(12345);

        // Assert - after removal, should get fresh limiter with full tokens
        manager.GetAvailableTokens(12345).Should().Be(5, "new limiter should have full tokens");
    }

    [Fact]
    public async Task TryAcquire_ConcurrentSessionsAndRequests_ShouldBeThreadSafe()
    {
        // Arrange
        var manager = new RateLimiterManager(maxRequestsPerMinute: 10);
        var results = new System.Collections.Concurrent.ConcurrentBag<(int processId, bool success)>();

        // Act - simulate concurrent requests from multiple processes
        var tasks = Enumerable.Range(1, 5) // 5 processes
            .SelectMany(processId => Enumerable.Range(0, 20) // 20 requests each
                .Select(_ => Task.Run(() =>
                {
                    var success = manager.TryAcquire(processId);
                    results.Add((processId, success));
                })))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - each process should have exactly 10 successful requests
        foreach (var processId in Enumerable.Range(1, 5))
        {
            var successCount = results.Count(r => r.processId == processId && r.success);
            successCount.Should().Be(10, $"process {processId} should have 10 successful requests");
        }
    }

    [Fact]
    public void RateLimiter_WithVeryLargeElapsedTime_ShouldNotOverflow()
    {
        // Validates fix: intervalsElapsed uses long to prevent int overflow
        var currentTime = DateTime.UtcNow;
        var limiter = new RateLimiter(10, TimeSpan.FromMinutes(1), () => currentTime);

        // Exhaust all tokens
        for (var i = 0; i < 10; i++)
            limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeFalse();

        // Jump forward by a very large time (exceeding int.MaxValue milliseconds)
        currentTime = currentTime.AddDays(30);

        // Act - should refill tokens without overflow
        var result = limiter.TryAcquire();

        // Assert
        result.Should().BeTrue("tokens should be refilled after large time gap without overflow");
    }
}
