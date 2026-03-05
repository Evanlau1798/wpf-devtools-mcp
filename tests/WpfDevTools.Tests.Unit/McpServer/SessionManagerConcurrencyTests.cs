using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for SessionManager concurrency and cleanup issues
/// </summary>
public class SessionManagerConcurrencyTests
{
    [Fact]
    public void CleanupDeadSessions_ShouldRemoveSessionsForDeadProcesses()
    {
        // Arrange
        var manager = new SessionManager();

        // Add a session for a process that doesn't exist
        var deadProcessId = 99999; // Very unlikely to exist
        manager.AddSession(deadProcessId);

        // Verify session was added
        manager.HasSession(deadProcessId).Should().BeTrue();

        // Act - Call cleanup method (to be implemented)
        var cleanupMethod = typeof(SessionManager).GetMethod("CleanupDeadSessions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        cleanupMethod.Should().NotBeNull("CleanupDeadSessions method should exist");
        cleanupMethod!.Invoke(manager, null);

        // Assert - Dead session should be removed
        manager.HasSession(deadProcessId).Should().BeFalse(
            "session for non-existent process should be removed");
    }

    [Fact]
    public void CleanupDeadSessions_ShouldKeepSessionsForLiveProcesses()
    {
        // Arrange
        var manager = new SessionManager();

        // Add a session for current process (guaranteed to be alive)
        var currentProcessId = Process.GetCurrentProcess().Id;
        manager.AddSession(currentProcessId);

        // Act - Call cleanup method
        var cleanupMethod = typeof(SessionManager).GetMethod("CleanupDeadSessions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        cleanupMethod!.Invoke(manager, null);

        // Assert - Live session should be kept
        manager.HasSession(currentProcessId).Should().BeTrue(
            "session for live process should be kept");

        // Cleanup
        manager.RemoveSession(currentProcessId);
    }

    [Fact]
    public void AddSession_WithMaxSessionsReached_ShouldThrowException()
    {
        // Arrange
        var manager = new SessionManager();

        // Add 50 sessions (MaxSessions limit)
        for (int i = 1; i <= 50; i++)
        {
            manager.AddSession(100000 + i);
        }

        // Act & Assert - Adding 51st session should throw
        var act = () => manager.AddSession(200000);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Maximum session limit*");
    }
}
