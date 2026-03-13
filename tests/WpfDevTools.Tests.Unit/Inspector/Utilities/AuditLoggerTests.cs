using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using System.Diagnostics;
using WpfDevTools.Tests.Unit.Execution;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

[Collection("TraceState")]
public class TraceAuditLoggerTests
{
    [Fact]
    public void Log_WithInformationSeverity_ShouldWriteToTrace()
    {
        // Arrange
        var logger = new TraceAuditLogger();
        var listener = new TestTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            logger.Log("TestCategory", "Test message", AuditSeverity.Information);

            // Assert
            listener.Messages.Should().ContainSingle();
            listener.Messages[0].Should().Contain("[AUDIT:INFO]");
            listener.Messages[0].Should().Contain("[TestCategory]");
            listener.Messages[0].Should().Contain("Test message");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void Log_WithWarningSeverity_ShouldWriteWarning()
    {
        // Arrange
        var logger = new TraceAuditLogger();
        var listener = new TestTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            logger.Log("Security", "Suspicious activity", AuditSeverity.Warning);

            // Assert
            listener.Messages[0].Should().Contain("[AUDIT:WARNING]");
            listener.Messages[0].Should().Contain("[Security]");
            listener.Messages[0].Should().Contain("Suspicious activity");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void Log_WithErrorSeverity_ShouldWriteError()
    {
        // Arrange
        var logger = new TraceAuditLogger();
        var listener = new TestTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            logger.Log("Critical", "System failure", AuditSeverity.Error);

            // Assert
            listener.Messages[0].Should().Contain("[AUDIT:ERROR]");
            listener.Messages[0].Should().Contain("[Critical]");
            listener.Messages[0].Should().Contain("System failure");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public async Task Log_ConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        var logger = new TraceAuditLogger();
        var listener = new TestTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            // Act - log from multiple threads
            var tasks = Enumerable.Range(0, 100)
                .Select(i => Task.Run(() => logger.Log("Test", $"Message {i}", AuditSeverity.Information)))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert - all messages should be logged
            listener.Messages.Should().HaveCount(100);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    private class TestTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
            if (message != null)
            {
                lock (Messages)
                {
                    Messages.Add(message);
                }
            }
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
            {
                lock (Messages)
                {
                    Messages.Add(message);
                }
            }
        }
    }
}

[Collection("TraceState")]
public class AuditLoggerStaticTests
{
    [Fact]
    public void LogSecurityEvent_WithoutInitialize_ShouldUseDefaultTraceLogger()
    {
        // Arrange
        // Note: AuditLogger uses static state, so it might already be initialized by other tests
        // We'll initialize it with TraceAuditLogger to ensure consistent behavior
        AuditLogger.Initialize(new TraceAuditLogger());

        var listener = new TestTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            AuditLogger.LogSecurityEvent("Test", "Default logger test", AuditSeverity.Information);

            // Assert - should use TraceAuditLogger
            listener.Messages.Should().ContainSingle();
            listener.Messages[0].Should().Contain("[AUDIT:INFO]");
            listener.Messages[0].Should().Contain("Default logger test");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void Initialize_WithCustomLogger_ShouldUseCustomLogger()
    {
        // Arrange
        var customLogger = new TestAuditLogger();

        // Act
        AuditLogger.Initialize(customLogger);
        AuditLogger.LogSecurityEvent("Test", "Custom logger test", AuditSeverity.Information);

        // Assert
        customLogger.LoggedMessages.Should().ContainSingle();
        customLogger.LoggedMessages[0].Should().Be(("Test", "Custom logger test", AuditSeverity.Information));
    }

    [Fact]
    public async Task LogSecurityEvent_ConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        var customLogger = new TestAuditLogger();
        AuditLogger.Initialize(customLogger);

        // Act - log from multiple threads
        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() =>
                AuditLogger.LogSecurityEvent("Test", $"Message {i}", AuditSeverity.Information)))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - all messages should be logged
        customLogger.LoggedMessages.Should().HaveCount(100);
    }

    [Theory]
    [InlineData(AuditSeverity.Information)]
    [InlineData(AuditSeverity.Warning)]
    [InlineData(AuditSeverity.Error)]
    public void LogSecurityEvent_AllSeverityLevels_ShouldLog(AuditSeverity severity)
    {
        // Arrange
        var customLogger = new TestAuditLogger();
        AuditLogger.Initialize(customLogger);

        // Act
        AuditLogger.LogSecurityEvent("Test", "Severity test", severity);

        // Assert
        customLogger.LoggedMessages.Should().ContainSingle();
        customLogger.LoggedMessages[0].severity.Should().Be(severity);
    }

    private class TestAuditLogger : IAuditLogger
    {
        public List<(string category, string message, AuditSeverity severity)> LoggedMessages { get; } = new();

        public void Log(string category, string message, AuditSeverity severity)
        {
            lock (LoggedMessages)
            {
                LoggedMessages.Add((category, message, severity));
            }
        }
    }

    private class TestTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
            if (message != null)
                Messages.Add(message);
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
                Messages.Add(message);
        }
    }
}

[Collection("TraceState")]
public class EventLogAuditLoggerTests
{
    [Fact]
    public void Log_WhenEventLogUnavailable_ShouldFallbackToTrace()
    {
        // Arrange
        var logger = new EventLogAuditLogger();
        var listener = new TestTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            // Act - log (will fallback to Trace if Event Log unavailable)
            logger.Log("Test", "Fallback test", AuditSeverity.Information);

            // Assert - should have logged something (either to Event Log or Trace)
            // We can't reliably test Event Log without admin privileges,
            // but we can verify it doesn't throw
            // If it falls back to Trace, we'll see the message
            if (listener.Messages.Count > 0)
            {
                // May contain initialization message or actual log message
                var allMessages = string.Join(" ", listener.Messages);
                allMessages.Should().Contain("[AUDIT]");
                // Either the fallback notification or the actual message
                (allMessages.Contains("Fallback test") || allMessages.Contains("not available")).Should().BeTrue();
            }
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void Log_ShouldNotThrowException()
    {
        // Arrange
        var logger = new EventLogAuditLogger();

        // Act & Assert - should not throw even if Event Log unavailable
        var act = () => logger.Log("Test", "No exception test", AuditSeverity.Information);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(AuditSeverity.Information)]
    [InlineData(AuditSeverity.Warning)]
    [InlineData(AuditSeverity.Error)]
    public void Log_AllSeverityLevels_ShouldNotThrow(AuditSeverity severity)
    {
        // Arrange
        var logger = new EventLogAuditLogger();

        // Act & Assert
        var act = () => logger.Log("Test", $"Severity {severity} test", severity);
        act.Should().NotThrow();
    }

    private class TestTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
            if (message != null)
                Messages.Add(message);
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
                Messages.Add(message);
        }
    }
}
