using System.Diagnostics;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.TestSupport;

public sealed class ConditionWaiterTests
{
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
