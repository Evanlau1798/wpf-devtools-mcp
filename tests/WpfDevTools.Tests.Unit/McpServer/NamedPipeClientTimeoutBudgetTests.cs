using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Security;
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

    [Fact]
    public async Task ConnectAsync_WithAuthenticatedClientAndPlaintextInspectorHost_ShouldReturnTimeout()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var authManager = new AuthenticationManager(() => secret);

        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClient(pid, authManager);

        var connected = await client.ConnectAsync(TimeSpan.FromMilliseconds(300));

        connected.Should().BeFalse();
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.Timeout);
    }
}
