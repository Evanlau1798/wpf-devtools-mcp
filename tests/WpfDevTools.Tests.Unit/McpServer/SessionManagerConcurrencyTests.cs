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
        using var manager = new SessionManager();

        // Add a session for a process that doesn't exist
        var deadProcessId = 99999; // Very unlikely to exist
        manager.AddSession(deadProcessId);

        // Verify session was added
        manager.HasSession(deadProcessId).Should().BeTrue();

        // Act - Call cleanup method directly (internal via InternalsVisibleTo)
        manager.CleanupDeadSessions();

        // Assert - Dead session should be removed
        manager.HasSession(deadProcessId).Should().BeFalse(
            "session for non-existent process should be removed");
    }

    [Fact]
    public void CleanupDeadSessions_ShouldKeepSessionsForLiveProcesses()
    {
        // Arrange
        using var manager = new SessionManager();

        // Add a session for current process (guaranteed to be alive)
        var currentProcessId = Process.GetCurrentProcess().Id;
        manager.AddSession(currentProcessId);

        // Act - Call cleanup method directly (internal via InternalsVisibleTo)
        manager.CleanupDeadSessions();

        // Assert - Live session should be kept
        manager.HasSession(currentProcessId).Should().BeTrue(
            "session for live process should be kept");

        // Cleanup
        manager.RemoveSession(currentProcessId);
    }

    [Fact]
    public void CleanupDeadSessions_ShouldRemoveSessionWhenProcessIdentityChangesForSamePid()
    {
        var processId = 123456;
        var currentIdentity = new SessionManager.ProcessIdentity(
            processId,
            StartTimeUtcTicks: 100);
        using var manager = CreateManagerWithProcessIdentityProvider(_ => currentIdentity);

        manager.AddSession(processId);
        currentIdentity = new SessionManager.ProcessIdentity(
            processId,
            StartTimeUtcTicks: 200);

        manager.CleanupDeadSessions();

        manager.HasSession(processId).Should().BeFalse(
            "a reused PID with a different process start time is not the same inspector target");
    }

    [Fact]
    public void CleanupDeadSessions_ShouldKeepSessionWhenProcessIdentityMatches()
    {
        var processId = 123457;
        var currentIdentity = new SessionManager.ProcessIdentity(
            processId,
            StartTimeUtcTicks: 100);
        using var manager = CreateManagerWithProcessIdentityProvider(_ => currentIdentity);

        manager.AddSession(processId);
        manager.CleanupDeadSessions();

        manager.HasSession(processId).Should().BeTrue(
            "a live process with the same captured identity should retain its session");
    }

    [Fact]
    public void CleanupDeadSessions_ShouldRemoveSessionWhenCurrentProcessIdentityIsIncomplete()
    {
        var processId = 123460;
        var currentIdentity = new SessionManager.ProcessIdentity(
            processId,
            StartTimeUtcTicks: 100);
        using var manager = CreateManagerWithProcessIdentityProvider(_ => currentIdentity);

        manager.AddSession(processId);
        currentIdentity = new SessionManager.ProcessIdentity(
            processId,
            StartTimeUtcTicks: null);

        manager.CleanupDeadSessions();

        manager.HasSession(processId).Should().BeFalse(
            "PID-only identity is not enough to prove the session still belongs to the same process instance");
    }

    [Fact]
    public void CleanupDeadSessions_ShouldNotRemoveReplacementSessionForSamePid()
    {
        var processId = 123458;
        SessionManager.ProcessIdentity? currentIdentity = new(
            processId,
            StartTimeUtcTicks: 100);
        using var manager = CreateManagerWithProcessIdentityProvider(_ => currentIdentity);

        manager.AddSession(processId);
        currentIdentity = null;

        manager.CleanupDeadSessions(beforeDeadSessionRemoval: () =>
        {
            currentIdentity = new SessionManager.ProcessIdentity(
                processId,
                StartTimeUtcTicks: 200);
            manager.RemoveSession(processId);
            manager.AddSession(processId);
        });

        manager.HasSession(processId).Should().BeTrue(
            "cleanup must not remove a newer session that replaced the dead session after collection");
        manager.CleanupDeadSessions();
        manager.HasSession(processId).Should().BeTrue(
            "the replacement session identity still matches the current process identity");
    }

    [Fact]
    public void CleanupIdleSessions_ShouldNotRemoveReplacementSessionForSamePid()
    {
        var processId = 123459;
        var now = new DateTimeOffset(2026, 5, 21, 0, 0, 0, TimeSpan.Zero);
        SessionManager.ProcessIdentity? currentIdentity = new(
            processId,
            StartTimeUtcTicks: 100);
        using var manager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => now,
            processIdentityProvider: _ => currentIdentity);

        manager.AddSession(processId);
        now += McpServerConfiguration.SessionIdleTimeout + TimeSpan.FromSeconds(1);

        manager.CleanupIdleSessions(
            McpServerConfiguration.SessionIdleTimeout,
            beforeIdleSessionRemoval: () =>
            {
                currentIdentity = new SessionManager.ProcessIdentity(
                    processId,
                    StartTimeUtcTicks: 200);
                manager.RemoveSession(processId);
                manager.AddSession(processId);
            });

        manager.HasSession(processId).Should().BeTrue(
            "idle cleanup must not remove a newer session that replaced an idle session after collection");
    }

    [Fact]
    public void AddSession_WithMaxSessionsReached_ShouldThrowException()
    {
        // Arrange
        using var manager = new SessionManager();

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

    private static SessionManager CreateManagerWithProcessIdentityProvider(
        Func<int, SessionManager.ProcessIdentity?> processIdentityProvider)
        => new(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: null,
            processIdentityProvider: processIdentityProvider);
}
