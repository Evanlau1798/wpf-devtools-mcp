using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ConnectToolTimeoutBudgetTests
{
    [Fact]
    public void GetRemainingPipeConnectTimeout_WhenElapsedIsFiveSeconds_ShouldReturnTwentyFiveSeconds()
    {
        var remaining = ConnectTool.GetRemainingPipeConnectTimeout(
            elapsed: TimeSpan.FromSeconds(5),
            totalTimeout: TimeSpan.FromSeconds(30));

        remaining.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public void GetRemainingPipeConnectTimeout_WhenElapsedConsumesBudget_ShouldReturnZero()
    {
        var remaining = ConnectTool.GetRemainingPipeConnectTimeout(
            elapsed: TimeSpan.FromSeconds(30),
            totalTimeout: TimeSpan.FromSeconds(30));

        remaining.Should().Be(TimeSpan.Zero,
            "connect must not start a fresh 30-second pipe handshake after the overall connect budget is already exhausted");
    }

    [Fact]
    public void GetRemainingPipeConnectTimeout_WhenElapsedExceedsBudget_ShouldReturnZero()
    {
        var remaining = ConnectTool.GetRemainingPipeConnectTimeout(
            elapsed: TimeSpan.FromSeconds(35),
            totalTimeout: TimeSpan.FromSeconds(30));

        remaining.Should().Be(TimeSpan.Zero);
    }
}
