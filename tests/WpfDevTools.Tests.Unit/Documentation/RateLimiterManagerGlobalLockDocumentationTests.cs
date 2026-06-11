using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class RateLimiterManagerGlobalLockDocumentationTests
{
    [Fact]
    public void RateLimiterManager_ShouldDocumentGlobalLockDecision()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/RateLimiter.cs"));

        content.Should().Contain("single manager lock is intentional");
        content.Should().Contain("McpServerConfiguration.MaxSessions");
        content.Should().Contain("100,000 TryAcquire calls");
        content.Should().Contain("profiling shows material contention");
    }
}