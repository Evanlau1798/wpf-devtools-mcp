using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Integration.E2E;

public sealed class McpStdioClientTimeoutPathContractTests
{
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
