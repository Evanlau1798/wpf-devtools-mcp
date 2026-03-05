using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit;

public class RateLimiterTests
{
    [Fact]
    public void RefillTokens_ShouldNotBeAffectedByTimeSkew()
    {
        // Arrange: Create a rate limiter with 10 tokens per minute
        var limiter = new RateLimiter(10, TimeSpan.FromMinutes(1));

        // Act: Consume all tokens
        for (int i = 0; i < 10; i++)
        {
            limiter.TryAcquire().Should().BeTrue();
        }

        // All tokens consumed
        limiter.TryAcquire().Should().BeFalse();
        limiter.GetAvailableTokens().Should().Be(0);

        // Wait for refill interval to pass
        Thread.Sleep(TimeSpan.FromMinutes(1).Add(TimeSpan.FromMilliseconds(100)));

        // Assert: Tokens should be refilled
        limiter.GetAvailableTokens().Should().Be(10);
        limiter.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void RefillTokens_ShouldCalculateIntervalsCorrectly()
    {
        // Arrange: Create a rate limiter with 5 tokens per second
        var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));

        // Consume all tokens
        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire().Should().BeTrue();
        }

        // Wait for 2 seconds (should refill 2 * 5 = 10 tokens, capped at 5)
        Thread.Sleep(TimeSpan.FromSeconds(2).Add(TimeSpan.FromMilliseconds(100)));

        // Assert: Should have max tokens (5), not 10
        limiter.GetAvailableTokens().Should().Be(5);
    }

    [Fact]
    public void RefillTokens_ShouldHandlePartialIntervals()
    {
        // Arrange: Create a rate limiter with 10 tokens per minute
        var limiter = new RateLimiter(10, TimeSpan.FromMinutes(1));

        // Consume all tokens
        for (int i = 0; i < 10; i++)
        {
            limiter.TryAcquire().Should().BeTrue();
        }

        // Wait for half the interval (should not refill yet)
        Thread.Sleep(TimeSpan.FromSeconds(30));

        // Assert: No refill yet (partial interval)
        limiter.GetAvailableTokens().Should().Be(0);

        // Wait for the rest of the interval
        Thread.Sleep(TimeSpan.FromSeconds(30).Add(TimeSpan.FromMilliseconds(100)));

        // Assert: Now tokens should be refilled
        limiter.GetAvailableTokens().Should().Be(10);
    }
}
