using System.Diagnostics;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.TestSupport;

public sealed class ConditionWaiterTests
{
    [Fact]
    public async Task WaitForAsync_WhenNonCooperativeActionSucceedsAfterDeadline_ShouldRejectLateResult()
    {
        var act = () => ConditionWaiter.WaitForAsync(
            async _ =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                return true;
            },
            result => result,
            TimeSpan.FromMilliseconds(20),
            "The condition missed its deadline.");

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("The condition missed its deadline.*");
    }

    [Fact]
    public async Task WaitForAsync_WhenActionExceedsDeadline_ShouldCancelActionAndThrowPromptly()
    {
        var cancellationObserved = false;
        var stopwatch = Stopwatch.StartNew();

        var act = () => ConditionWaiter.WaitForAsync(
            async cancellationToken =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    return false;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved = true;
                    throw;
                }
            },
            result => result,
            TimeSpan.FromMilliseconds(100),
            "Condition was not satisfied.");

        await act.Should().ThrowAsync<TimeoutException>();

        cancellationObserved.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}
