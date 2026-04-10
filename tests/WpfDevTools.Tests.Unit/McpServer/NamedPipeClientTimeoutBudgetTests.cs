using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Tests.Unit.Execution;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
public class NamedPipeClientTimeoutBudgetTests
{
    [Fact]
    public async Task ConnectAsync_WithRetries_ShouldRespectTotalTimeoutBudget()
    {
        using var client = new NamedPipeClient(global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId(), $"WpfDevTools_Test_{Guid.NewGuid():N}");
        var timeout = TimeSpan.FromMilliseconds(150);

        var sw = Stopwatch.StartNew();
        var connected = await client.ConnectAsync(timeout, maxRetries: 3);
        sw.Stop();

        connected.Should().BeFalse();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(800),
            "retry attempts must share the same timeout budget instead of spending the full timeout on each attempt");
    }
}
