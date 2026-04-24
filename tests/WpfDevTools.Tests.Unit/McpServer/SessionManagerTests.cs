using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Tests.Unit.McpServer;

public class SessionManagerTests
{
    [Fact]
    public void AddSession_WithAuthManager_CreatesSessionWithPipeClient()
    {
        // Arrange
        var authManager = new AuthenticationManager(() => Convert.ToBase64String(new byte[32]));
        using var sm = new SessionManager(authManager: authManager);

        // Act
        sm.AddSession(99999);
        var client = sm.GetPipeClient(99999);

        // Assert
        client.Should().NotBeNull();
        sm.RemoveSession(99999);
    }

    [Fact]
    public void Constructor_WithAuthAndCert_AcceptsParameters()
    {
        // Arrange & Act
        var authManager = new AuthenticationManager(() => Convert.ToBase64String(new byte[32]));
        var certManager = new CertificateManager(
            Path.Combine(Path.GetTempPath(), $"test_certs_{Guid.NewGuid()}"));
        using var sm = new SessionManager(authManager: authManager, certManager: certManager);

        // Assert
        sm.GetActiveSessionCount().Should().Be(0);
    }

    [Fact]
    public void AddSession_WithValidProcessId_ShouldAddToActiveSessions()
    {
        // Arrange
        using var manager = new SessionManager();
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
        using var manager = new SessionManager();
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
        using var manager = new SessionManager();
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
        using var manager = new SessionManager();

        // Act & Assert
        var act = () => manager.RemoveSession(99999);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetAllSessions_WithMultipleSessions_ShouldReturnAllProcessIds()
    {
        // Arrange
        using var manager = new SessionManager();
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
        var currentTime = DateTimeOffset.UtcNow;
        using var manager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => currentTime);
        var processId = 12345;
        manager.AddSession(processId);
        var initialTime = manager.GetLastActivityTime(processId);

        currentTime = currentTime.AddMinutes(1);

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
        var currentTime = DateTimeOffset.UtcNow;
        using var manager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => currentTime);
        manager.AddSession(100);
        currentTime = currentTime.AddMinutes(5);
        manager.AddSession(200); // Fresh session

        // Act
        var idleSessions = manager.GetIdleSessions(TimeSpan.FromMinutes(1));

        // Assert
        idleSessions.Should().Contain(100);
        idleSessions.Should().NotContain(200);
    }

    [Fact]
    public void Dispose_ImmediatelyAfterCreation_ShouldNotThrow()
    {
        // Validates C2: Dispose during cleanup timer shouldn't cause race condition
        var manager = new SessionManager();
        manager.AddSession(100);

        // Act: dispose immediately (timer callback may be pending)
        var act = () => manager.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ShouldPreventFurtherOperations()
    {
        // Arrange
        var manager = new SessionManager();
        manager.Dispose();

        // Act & Assert
        var act = () => manager.HasSession(100);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void GetPipeClient_WhenNoSession_ShouldReturnNull()
    {
        // Validates C4: GetPipeClient returns null for non-existent sessions
        using var manager = new SessionManager();

        // Act
        var client = manager.GetPipeClient(99999);

        // Assert
        client.Should().BeNull();
    }
}
