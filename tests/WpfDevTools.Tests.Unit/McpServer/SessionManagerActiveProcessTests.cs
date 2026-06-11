using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class SessionManagerActiveProcessTests
{
    [Fact]
    public void AddSession_WhenNoActiveProcessExists_ShouldSelectFirstSessionAsActive()
    {
        using var manager = new SessionManager();

        manager.AddSession(101);

        manager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(101);
    }

    [Fact]
    public void SetActiveProcess_ShouldSwitchToRequestedConnectedSession()
    {
        using var manager = new SessionManager();
        manager.AddSession(101);
        manager.AddSession(202);

        manager.SetActiveProcess(202);

        manager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(202);
    }

    [Fact]
    public void RemoveSession_WhenRemovingActiveProcess_ShouldClearActiveSelection()
    {
        using var manager = new SessionManager();
        manager.AddSession(101);

        manager.RemoveSession(101);

        manager.TryGetActiveProcessId(out _).Should().BeFalse();
    }
}
