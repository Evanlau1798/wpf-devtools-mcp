using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public class CertificateValidityWindowTests
{
    [Fact]
    public void Contains_WhenTimestampIsWithinWindow_ShouldReturnTrue()
    {
        var window = new CertificateValidityWindow(
            new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero));

        var result = window.Contains(new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero));

        result.Should().BeTrue();
    }

    [Fact]
    public void Contains_WhenTimestampIsBeforeWindow_ShouldReturnFalse()
    {
        var window = new CertificateValidityWindow(
            new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero));

        var result = window.Contains(new DateTimeOffset(2026, 3, 8, 7, 59, 59, TimeSpan.Zero));

        result.Should().BeFalse();
    }

    [Fact]
    public void Contains_WhenTimestampIsAfterWindow_ShouldReturnFalse()
    {
        var window = new CertificateValidityWindow(
            new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero));

        var result = window.Contains(new DateTimeOffset(2026, 3, 8, 10, 0, 1, TimeSpan.Zero));

        result.Should().BeFalse();
    }
}
