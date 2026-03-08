using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public class SessionAndRateLimiterTimeContractTests
{
    [Fact]
    public void SessionManager_GetLastActivityTime_ShouldReturnDateTimeOffset()
    {
        var method = typeof(SessionManager).GetMethod(nameof(SessionManager.GetLastActivityTime));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(DateTimeOffset));
    }

    [Fact]
    public void SessionInfo_LastActivity_ShouldUseDateTimeOffset()
    {
        var sessionInfoType = typeof(SessionManager).GetNestedType(
            "SessionInfo",
            BindingFlags.NonPublic);

        sessionInfoType.Should().NotBeNull();
        sessionInfoType!
            .GetProperty("LastActivity", BindingFlags.Instance | BindingFlags.Public)!
            .PropertyType
            .Should()
            .Be(typeof(DateTimeOffset));
    }

    [Fact]
    public void RateLimiterEntry_LastAccessed_ShouldUseDateTimeOffset()
    {
        var entryType = typeof(RateLimiterManager).GetNestedType(
            "RateLimiterEntry",
            BindingFlags.NonPublic);

        entryType.Should().NotBeNull();
        entryType!
            .GetProperty("LastAccessed", BindingFlags.Instance | BindingFlags.Public)!
            .PropertyType
            .Should()
            .Be(typeof(DateTimeOffset));
    }
}
