using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class SessionManagerTests
{
    [Fact]
    public void AddSession_WithValidProcessId_ShouldAddToActiveSessions()
    {
        // Arrange
        var manager = new SessionManager();
        var processId = 12345;

        // Act
        manager.AddSession(processId);

        // Assert
        manager.HasSession(processId).Should().BeTrue();
        manager.GetActiveSessionCount().Should().Be(1);
    }

    [Fact]
    public void AddSession_WithDuplicateProcessId_ShouldThrowException()
    {
        // Arrange
        var manager = new SessionManager();
        var processId = 12345;
        manager.AddSession(processId);

        // Act & Assert
        var act = () => manager.AddSession(processId);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public void RemoveSession_WithExistingProcessId_ShouldRemoveFromActiveSessions()
    {
        // Arrange
        var manager = new SessionManager();
        var processId = 12345;
        manager.AddSession(processId);

        // Act
        manager.RemoveSession(processId);

        // Assert
        manager.HasSession(processId).Should().BeFalse();
        manager.GetActiveSessionCount().Should().Be(0);
    }

    [Fact]
    public void RemoveSession_WithNonExistingProcessId_ShouldNotThrow()
    {
        // Arrange
        var manager = new SessionManager();

        // Act & Assert
        var act = () => manager.RemoveSession(99999);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetAllSessions_WithMultipleSessions_ShouldReturnAllProcessIds()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);
        manager.AddSession(300);

        // Act
        var sessions = manager.GetAllSessions();

        // Assert
        sessions.Should().HaveCount(3);
        sessions.Should().Contain(new[] { 100, 200, 300 });
    }

    [Fact]
    public void UpdateLastActivity_WithExistingSession_ShouldUpdateTimestamp()
    {
        // Arrange
        var manager = new SessionManager();
        var processId = 12345;
        manager.AddSession(processId);

        var initialTime = manager.GetLastActivityTime(processId);

        // Wait to ensure timestamp can change (avoid same-tick updates)
        Task.Delay(10).Wait();

        // Act
        manager.UpdateLastActivity(processId);

        // Assert
        var updatedTime = manager.GetLastActivityTime(processId);
        updatedTime.Should().BeAfter(initialTime);
    }

    [Fact]
    public void GetIdleSessions_WithIdleTimeout_ShouldReturnIdleSessions()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);

        // Wait to ensure session becomes idle
        Task.Delay(150).Wait();

        manager.AddSession(200); // Fresh session

        // Act
        var idleSessions = manager.GetIdleSessions(TimeSpan.FromMilliseconds(100));

        // Assert
        idleSessions.Should().Contain(100);
        idleSessions.Should().NotContain(200);
    }
}
