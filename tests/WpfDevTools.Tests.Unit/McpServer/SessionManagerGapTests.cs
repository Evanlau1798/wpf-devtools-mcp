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
        using var manager = new SessionManager();

        // Act
        var act = () => manager.UpdateLastActivity(99999);

        // Assert - should silently do nothing
        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateLastActivity_WithNonExistentSession_ShouldNotCreateSession()
    {
        // Arrange
        using var manager = new SessionManager();

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
        using var manager = new SessionManager();

        // Act
        var result = manager.GetLastActivityTime(99999);

        // Assert
        Assert.Equal(DateTimeOffset.MinValue, result);
    }

    [Fact]
    public void GetLastActivityTime_AfterRemovingSession_ShouldReturnMinValue()
    {
        // Arrange
        using var manager = new SessionManager();
        manager.AddSession(12345);
        var activityTime = manager.GetLastActivityTime(12345);
        activityTime.Should().NotBe(DateTimeOffset.MinValue); // sanity check: was valid

        manager.RemoveSession(12345);

        // Act
        var result = manager.GetLastActivityTime(12345);

        // Assert
        Assert.Equal(DateTimeOffset.MinValue, result);
    }

    #endregion

    #region GetPipeClient for non-existent processId

    [Fact]
    public void GetPipeClient_WithNonExistentProcessId_ShouldReturnNull()
    {
        // Arrange
        using var manager = new SessionManager();

        // Act
        var result = manager.GetPipeClient(99999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetPipeClient_AfterRemovingSession_ShouldReturnNull()
    {
        // Arrange
        using var manager = new SessionManager();
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
        using var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);
        manager.AddSession(300);
        manager.GetActiveSessionCount().Should().Be(3);

        // Act
        manager.Dispose();

        // Assert - all public methods should throw ObjectDisposedException after dispose
        var act = () => manager.GetActiveSessionCount();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_WithMultipleClients_ShouldRejectSubsequentCalls()
    {
        // Arrange
        using var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);

        // Act
        manager.Dispose();

        // Assert - public methods should throw ObjectDisposedException after dispose
        var act = () => manager.GetPipeClient(100);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        using var manager = new SessionManager();
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
        using var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);

        // Act
        manager.Dispose();
        manager.Dispose();

        // Assert - should stay disposed and reject operations
        var act = () => manager.GetActiveSessionCount();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_WithNoSessions_ShouldNotThrow()
    {
        // Arrange
        using var manager = new SessionManager();

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
        using var manager = new SessionManager();

        // Act
        var idleSessions = manager.GetIdleSessions(TimeSpan.FromMinutes(1));

        // Assert
        idleSessions.Should().BeEmpty();
    }

    [Fact]
    public void GetIdleSessions_WithZeroTimeout_ShouldReturnAllSessions()
    {
        // Arrange
        using var manager = new SessionManager();
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
        using var manager = new SessionManager();

        // Act
        var sessions = manager.GetAllSessions();

        // Assert
        sessions.Should().BeEmpty();
    }

    [Fact]
    public void GetAllSessions_AfterDispose_ShouldThrowObjectDisposed()
    {
        // Arrange
        using var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);
        manager.Dispose();

        // Act & Assert
        var act = () => manager.GetAllSessions();
        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion
}
