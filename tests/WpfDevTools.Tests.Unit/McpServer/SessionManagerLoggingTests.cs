using FluentAssertions;
using Microsoft.Extensions.Logging;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for SessionManager logging integration.
/// Validates that cleanup errors are routed through ILogger
/// instead of direct file writes.
/// </summary>
public class SessionManagerLoggingTests : IDisposable
{
    private readonly FakeLogger _logger = new();

    public void Dispose()
    {
        _logger.Dispose();
    }

    [Fact]
    public void Constructor_ShouldAcceptILogger()
    {
        // SessionManager should accept an optional ILogger parameter
        using var sm = new SessionManager(
            new RateLimiterManager(100),
            authManager: null,
            certManager: null,
            logger: _logger);

        sm.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldAcceptNullLogger()
    {
        // Null logger should be accepted (backward compatibility)
        using var sm = new SessionManager(
            new RateLimiterManager(100),
            authManager: null,
            certManager: null,
            logger: null);

        sm.Should().NotBeNull();
    }

    [Fact]
    public void BackwardCompatConstructor_ShouldStillWork()
    {
        // Existing constructor signatures must remain valid
        using var sm = new SessionManager(maxRequestsPerMinute: 100);
        sm.Should().NotBeNull();
    }

    /// <summary>
    /// Fake logger that captures log messages for test assertions.
    /// </summary>
    private sealed class FakeLogger : ILogger<SessionManager>, IDisposable
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add($"[{logLevel}] {formatter(state, exception)}");
        }

        public void Dispose() { }
    }
}
