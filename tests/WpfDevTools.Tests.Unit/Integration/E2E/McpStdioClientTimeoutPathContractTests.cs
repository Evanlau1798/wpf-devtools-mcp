using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Integration.E2E;

public sealed class McpStdioClientTimeoutPathContractTests
{
    [Fact]
    public void InitializeNotification_ShouldHaveAnIndependentWriteDeadline()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "tests/WpfDevTools.Tests.Integration/E2E/McpStdioClient.cs"));

        content.Should().Contain(
            "SendNotificationAsync(\"notifications/initialized\", timeoutMs: 30000, ct)");
        content.Should().Contain(
            "SendNotificationAsync(string method, int timeoutMs, CancellationToken ct)");
    }

    [Fact]
    public void SendRequestAsync_ShouldApplyItsDeadlineBeforeWritingToTheServer()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "tests/WpfDevTools.Tests.Integration/E2E/McpStdioClient.cs"));

        content.IndexOf("CancellationTokenSource.CreateLinkedTokenSource", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf("await SendJsonLineAsync(payload", StringComparison.Ordinal));
        content.Should().Contain("await SendJsonLineAsync(payload, requestCts.Token)");
        content.Should().Contain("await _writeLock.WaitAsync(ct)");
        content.Should().Contain("WriteLineAsync(json.AsMemory(), ct)");
        content.Should().Contain("FlushAsync(ct)");
    }

    [Fact]
    public void SendRequestAsync_ShouldNotKeepUnreachableFallbackTimeoutThrow()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "tests/WpfDevTools.Tests.Integration/E2E/McpStdioClient.cs"));

        content.Should().Contain("Server state: {serverState}. Stderr tail:",
            "the reachable timeout path should preserve diagnostics for debugging server hangs");
        content.Should().NotContain(
            "throw new TimeoutException(\r\n            $\"Timed out waiting for response to '{method}' (id={id})\");",
            "Task.WhenAny with a cancellation-token delay either returns the response task or throws OperationCanceledException");
    }
}
