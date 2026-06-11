using FluentAssertions;
using WpfDevTools.Mcp.Server;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public class RateLimiterCleanupGuardTests
{
    [Fact]
    public void Execute_WhenDisposed_ShouldSkipCleanupAction()
    {
        var invoked = false;

        RateLimiterCleanupGuard.Execute(
            isDisposed: true,
            cleanupAction: () => invoked = true,
            onError: _ => { });

        invoked.Should().BeFalse();
    }

    [Fact]
    public void Execute_WhenCleanupThrows_ShouldSwallowAndReportError()
    {
        Exception? captured = null;

        var act = () => RateLimiterCleanupGuard.Execute(
            isDisposed: false,
            cleanupAction: () => throw new InvalidOperationException("boom"),
            onError: ex => captured = ex);

        act.Should().NotThrow();
        captured.Should().BeOfType<InvalidOperationException>();
    }
}
