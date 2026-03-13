using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Navigation;
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

    [Fact]
    public void SessionManager_ShouldSetGetAndClearActiveSnapshotState()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(1001);

        sessionManager.SetActiveSnapshotId(1001, "snapshot_123");
        sessionManager.TryGetNavigationState(1001, out var state).Should().BeTrue();
        state!.ActiveSnapshotId.Should().Be("snapshot_123");

        sessionManager.ClearActiveSnapshotId(1001);
        sessionManager.TryGetNavigationState(1001, out state).Should().BeTrue();
        state!.ActiveSnapshotId.Should().BeNull();
    }

    [Fact]
    public void SessionManager_ShouldSetGetAndClearActiveTraceState()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(1002);
        var traceState = new ActiveTraceNavigationState("Click", "Button_1", DateTimeOffset.UtcNow);

        sessionManager.SetActiveTraceState(1002, traceState);
        sessionManager.TryGetNavigationState(1002, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().BeEquivalentTo(traceState);

        sessionManager.ClearActiveTraceState(1002);
        sessionManager.TryGetNavigationState(1002, out state).Should().BeTrue();
        state!.ActiveTrace.Should().BeNull();
    }

    [Fact]
    public void SessionManager_NavigationState_ShouldRemainIsolatedPerProcess()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(1003);
        sessionManager.AddSession(1004);

        sessionManager.SetActiveSnapshotId(1003, "snapshot_a");
        sessionManager.SetActiveTraceState(1004, new ActiveTraceNavigationState("MouseDown", "Panel_1", DateTimeOffset.UtcNow));

        sessionManager.TryGetNavigationState(1003, out var firstState).Should().BeTrue();
        sessionManager.TryGetNavigationState(1004, out var secondState).Should().BeTrue();

        firstState!.ActiveSnapshotId.Should().Be("snapshot_a");
        firstState.ActiveTrace.Should().BeNull();
        secondState!.ActiveSnapshotId.Should().BeNull();
        secondState.ActiveTrace.Should().NotBeNull();
        secondState.ActiveTrace!.EventName.Should().Be("MouseDown");
    }
}
