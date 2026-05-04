using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit;

public class RateLimiterTests
{
    [Fact]
    public void RefillTokens_ShouldNotBeAffectedByTimeSkew()
    {
        // Arrange: Create a rate limiter with 10 tokens per minute, using controllable time
        var currentTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limiter = new RateLimiter(10, TimeSpan.FromMinutes(1), () => currentTime);

        // Act: Consume all tokens
        for (int i = 0; i < 10; i++)
        {
            limiter.TryAcquire().Should().BeTrue();
        }

        // All tokens consumed
        limiter.TryAcquire().Should().BeFalse();
        limiter.GetAvailableTokens().Should().Be(0);

        // Advance time past refill interval
        currentTime = currentTime.AddMinutes(1).AddMilliseconds(100);

        // Assert: Tokens should be refilled
        limiter.GetAvailableTokens().Should().Be(10);
        limiter.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void RefillTokens_ShouldCalculateIntervalsCorrectly()
    {
        // Arrange: Create a rate limiter with 5 tokens per second
        var currentTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1), () => currentTime);

        // Consume all tokens
        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire().Should().BeTrue();
        }

        // Advance time by 2 seconds (should refill 2 * 5 = 10 tokens, capped at 5)
        currentTime = currentTime.AddSeconds(2).AddMilliseconds(100);

        // Assert: Should have max tokens (5), not 10
        limiter.GetAvailableTokens().Should().Be(5);
    }

    [Fact]
    public void RefillTokens_ShouldHandlePartialIntervals()
    {
        // Arrange: Create a rate limiter with 10 tokens per minute
        var currentTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limiter = new RateLimiter(10, TimeSpan.FromMinutes(1), () => currentTime);

        // Consume all tokens
        for (int i = 0; i < 10; i++)
        {
            limiter.TryAcquire().Should().BeTrue();
        }

        // Advance time by half the interval (should not refill yet)
        currentTime = currentTime.AddSeconds(30);

        // Assert: No refill yet (partial interval)
        limiter.GetAvailableTokens().Should().Be(0);

        // Advance time past the full interval
        currentTime = currentTime.AddSeconds(30).AddMilliseconds(100);

        // Assert: Now tokens should be refilled
        limiter.GetAvailableTokens().Should().Be(10);
    }

    [Fact]
    public async Task TryAcquire_WhenCalledConcurrently_ShouldNotGrantMoreThanAvailableTokens()
    {
        var limiter = new RateLimiter(5, TimeSpan.FromMinutes(1));
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () =>
            {
                await startGate.Task;
                return limiter.TryAcquire();
            }))
            .ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(workers);

        results.Count(acquired => acquired).Should().Be(5);
        limiter.GetAvailableTokens().Should().Be(0);
    }
}
