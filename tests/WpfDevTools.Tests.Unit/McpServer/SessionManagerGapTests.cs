using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Gap tests for SessionManager to improve code coverage.
/// Covers edge cases: non-existent sessions, disposal, idle session detection.
/// </summary>
public class SessionManagerGapTests
{
    #region UpdateLastActivity for non-existent session

    [Fact]
    public void UpdateLastActivity_WithNonExistentSession_ShouldNotThrow()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var act = () => manager.UpdateLastActivity(99999);

        // Assert - should silently do nothing
        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateLastActivity_WithNonExistentSession_ShouldNotCreateSession()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        manager.UpdateLastActivity(99999);

        // Assert - session should not be created
        manager.HasSession(99999).Should().BeFalse();
        manager.GetActiveSessionCount().Should().Be(0);
    }

    #endregion

    #region GetLastActivityTime for non-existent session

    [Fact]
    public void GetLastActivityTime_WithNonExistentSession_ShouldReturnMinValue()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var result = manager.GetLastActivityTime(99999);

        // Assert
        result.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void GetLastActivityTime_AfterRemovingSession_ShouldReturnMinValue()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(12345);
        var activityTime = manager.GetLastActivityTime(12345);
        activityTime.Should().NotBe(DateTime.MinValue); // sanity check: was valid

        manager.RemoveSession(12345);

        // Act
        var result = manager.GetLastActivityTime(12345);

        // Assert
        result.Should().Be(DateTime.MinValue);
    }

    #endregion

    #region GetPipeClient for non-existent processId

    [Fact]
    public void GetPipeClient_WithNonExistentProcessId_ShouldReturnNull()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var result = manager.GetPipeClient(99999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetPipeClient_AfterRemovingSession_ShouldReturnNull()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(12345);
        manager.GetPipeClient(12345).Should().NotBeNull(); // sanity check

        manager.RemoveSession(12345);

        // Act
        var result = manager.GetPipeClient(12345);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Dispose with multiple clients

    [Fact]
    public void Dispose_WithMultipleClients_ShouldClearAllSessions()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);
        manager.AddSession(300);
        manager.GetActiveSessionCount().Should().Be(3);

        // Act
        manager.Dispose();

        // Assert - all sessions should be cleared
        manager.GetActiveSessionCount().Should().Be(0);
        manager.HasSession(100).Should().BeFalse();
        manager.HasSession(200).Should().BeFalse();
        manager.HasSession(300).Should().BeFalse();
    }

    [Fact]
    public void Dispose_WithMultipleClients_ShouldClearAllPipeClients()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);

        // Act
        manager.Dispose();

        // Assert - pipe clients should be null after dispose
        manager.GetPipeClient(100).Should().BeNull();
        manager.GetPipeClient(200).Should().BeNull();
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(12345);

        // Act - double dispose
        manager.Dispose();
        var act = () => manager.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldRemainDisposed()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);

        // Act
        manager.Dispose();
        manager.Dispose();

        // Assert - should stay clean
        manager.GetActiveSessionCount().Should().Be(0);
        manager.GetAllSessions().Should().BeEmpty();
    }

    [Fact]
    public void Dispose_WithNoSessions_ShouldNotThrow()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var act = () => manager.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region GetIdleSessions edge cases

    [Fact]
    public void GetIdleSessions_WithNoSessions_ShouldReturnEmpty()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var idleSessions = manager.GetIdleSessions(TimeSpan.FromMinutes(1));

        // Assert
        idleSessions.Should().BeEmpty();
    }

    [Fact]
    public void GetIdleSessions_WithZeroTimeout_ShouldReturnAllSessions()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);

        // Small wait to ensure sessions have non-zero age
        Thread.Sleep(10);

        // Act - zero timeout means all sessions are idle
        var idleSessions = manager.GetIdleSessions(TimeSpan.Zero);

        // Assert
        idleSessions.Should().HaveCount(2);
        idleSessions.Should().Contain(new[] { 100, 200 });
    }

    #endregion

    #region GetAllSessions edge cases

    [Fact]
    public void GetAllSessions_WithNoSessions_ShouldReturnEmpty()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var sessions = manager.GetAllSessions();

        // Assert
        sessions.Should().BeEmpty();
    }

    [Fact]
    public void GetAllSessions_AfterDispose_ShouldReturnEmpty()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);
        manager.Dispose();

        // Act
        var sessions = manager.GetAllSessions();

        // Assert
        sessions.Should().BeEmpty();
    }

    #endregion
}
